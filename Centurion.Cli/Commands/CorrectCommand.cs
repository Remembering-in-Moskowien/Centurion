using Centurion.Cli.Commands.Settings;
using Centurion.Abstractions;
using Centurion.Abstractions.Pipeline;
using Centurion.Core.Capabilities.Infrastructure;using Centurion.Core.Workflow.Factories;using Centurion.Models.Ass;
using Centurion.Models.Workflow;
using Centurion.Core.Workflow.Pipeline;using Centurion.Core.Workflow.Pipeline.Operators;using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;
using Centurion.Abstractions.Utils;
using Centurion.Core.Utils.Serialization;
using Centurion.Models.Console;
namespace Centurion.Cli.Commands;

/// <summary>
/// Calibration: correct an existing subtitle file against the source audio and/or a reference script.
/// </summary>
public sealed class CorrectCommand(
    ITempDirectoryManager tempManager,
    SpellCheckOperator spellCheckOp,
    FFmpegConvertOperator ffmpegOp,
    AudioPreprocessOperator audioPreprocessOp,
    VocalSeparationOperator vocalSepOp,
    ScriptLoaderOperator scriptLoaderOp,
    SubtitleTextCorrectorOperator textCorrectorOp,
    PipelineOperatorFactory operatorFactory,
    OverlapResolutionOperator overlapOp,
    CorrectionReportOperator reportOp,
    QualityReportOperator qualityReportOp,
    PipelineExecutor pipelineExecutor,
    ILogger<CorrectCommand> logger, ICenturionDocumentStore store) : AsyncCommand<CorrectSettings>
{
    /// <summary>
    /// 执行校正：按所选策略组装并运行校正管道，写出校正后的 ASS 字幕。
    /// </summary>
    /// <param name="context">Spectre 命令上下文。</param>
    /// <param name="settings">校正命令选项。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    protected override async Task<int> ExecuteAsync(CommandContext context, CorrectSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var strategy = ParseStrategy(settings.Strategy);
            Validate(settings, strategy);
            var needsAudio = strategy is CorrectionStrategy.TimelineOnly or CorrectionStrategy.Both;

            // 输入：Centurion 中间文件（含待校正的句子与词级时间轴）
            var inputPath = settings.CenturionFile.FullName;
            if (!File.Exists(inputPath))
                throw new FileNotFoundException($"Centurion intermediate file not found: {inputPath}", inputPath);

            await using var tempDir = await tempManager.CreateTempDirectoryAsync("correct_");

            var loadedDoc = await store.LoadAsync(inputPath, cancellationToken);
            var workflowContext = new SubtitleWorkflowContext(loadedDoc.Config) { State = loadedDoc.State };
            workflowContext.State.PipelineTempDirectory = tempDir.Path;

            var outputPath = settings.OutputFile?.FullName
                ?? CenturionFileIO.DefaultOutputPath(inputPath, "corrected");
            var audioForTimeline = needsAudio ? settings.AudioFile?.FullName : null;
            if (needsAudio && audioForTimeline is null)
                throw new ArgumentException("--audio is required for the selected correction strategy.");

            // 重建工作流配置：保留中间文件中的语言/设备/人声分离等设置，覆盖校正相关字段
            var previous = workflowContext.Config;
            var config = new WorkflowConfig
            {
                CommandName = "correct",
                InputFilePath = audioForTimeline ?? previous.SubtitleFilePath ?? inputPath,
                SubtitleFilePath = previous.SubtitleFilePath ?? inputPath,
                OutputFilePath = outputPath,
                ScriptFilePath = settings.ScriptFile?.FullName ?? previous.ScriptFilePath,
                CorrectStrategy = strategy,
                Language = settings.Language,
                MaxDriftMs = settings.MaxDrift,
                FuzzyThreshold = settings.FuzzyThreshold,
                HunspellDictionary = settings.HunspellDictionary,
                KaraokeMode = settings.Karaoke || previous.KaraokeMode,
                ShowSpeakerLabels = settings.ShowSpeakerLabels || previous.ShowSpeakerLabels,
                AudioPreprocess = new AudioPreprocessConfig
                {
                    EnableResampling = !settings.DisableAudioResampling,
                    EnableHighPass = !settings.DisableAudioHighPass,
                    EnableLoudnessNormalization = !settings.DisableAudioLoudness
                },
                VocalSeparation = settings.VocalSeparation || previous.VocalSeparation,
                VocalSeparationModel = settings.VocalSeparationModel,
                Device = settings.Device,
                SplitStrategy = previous.SplitStrategy,
                MaxSentenceLength = previous.MaxSentenceLength,
                TargetSentenceLength = previous.TargetSentenceLength,
                TranscriberEngine = previous.TranscriberEngine,
                TranscriberModel = previous.TranscriberModel,
                EnableAlignment = previous.EnableAlignment,
                AlignmentModel = previous.AlignmentModel,
                CacheDirectory = previous.CacheDirectory ?? "./cache"
            };
            workflowContext.Config = config;

            // correct DAG：文本分支（脚本加载→文本校正）与音频分支（转换→预处理→人声分离→
            // 说话人分割→对齐→重叠消解）并行，汇合后拼写检查 → 校正报告 → 质量报告
            var dag = BuildCorrectDag(
                scriptLoaderOp, textCorrectorOp, ffmpegOp, audioPreprocessOp, vocalSepOp,
                operatorFactory, overlapOp, spellCheckOp, reportOp, qualityReportOp,
                config, strategy, needsAudio, settings.SpellCheck);
            await pipelineExecutor.ExecuteAsync(dag, workflowContext, cancellationToken);

            // 保存校正后的中间文件（时间轴/文本修正全部写入状态）
            var outDoc = CenturionDocumentBuilder.Create(workflowContext, "correct", outputPath);
            await store.SaveAsync(outDoc, outputPath, cancellationToken);

            ConsoleServices.Output.WriteSuccess(ConsoleServices.T("Correction completed"));
            ConsoleServices.Output.WriteInfo(ConsoleServices.T("Build subtitles with: {0}", "Centurion build <file>.centurion.json"));
            return 0;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            FailLogGate.Log(logger, ex, "Correction pipeline execution failed.");
            return 1;
        }
    }

    /// <summary>
    /// 组装 correct DAG（pipeline graph 命令与 correct 命令共享的单一事实源）：
    /// 文本分支（Script Load → Text Correct）与音频分支（FFmpeg → 预处理 → 人声分离 →
    /// 说话人分割 → 强制对齐 → 重叠消解）按策略条件接入；两分支汇合后可选拼写检查 →
    /// 校正报告 → 质量报告。
    /// </summary>
    internal static PipelineDag BuildCorrectDag(
        ScriptLoaderOperator scriptLoaderOp,
        SubtitleTextCorrectorOperator textCorrectorOp,
        FFmpegConvertOperator ffmpegOp,
        AudioPreprocessOperator audioPreprocessOp,
        VocalSeparationOperator vocalSepOp,
        PipelineOperatorFactory operatorFactory,
        OverlapResolutionOperator overlapOp,
        SpellCheckOperator spellCheckOp,
        CorrectionReportOperator reportOp,
        QualityReportOperator qualityReportOp,
        Centurion.Models.Workflow.WorkflowConfig config,
        CorrectionStrategy strategy,
        bool needsAudio,
        bool runSpellCheck)
    {
        var builder = PipelineDag.CreateBuilder();
        var useText = strategy is CorrectionStrategy.TextOnly or CorrectionStrategy.Both;
        var join = new List<string>();

        if (useText)
        {
            builder
                .Add("Script Load", scriptLoaderOp, description: "加载参考脚本/台本")
                .Add("Text Correct", textCorrectorOp, dependsOn: ["Script Load"], description: "按台本校正文本");
            join.Add("Text Correct");
        }

        if (needsAudio)
        {
            builder
                .Add("FFmpeg Convert", ffmpegOp, description: "重采样/转码为统一音频")
                .Add("Audio Preprocess", audioPreprocessOp, dependsOn: ["FFmpeg Convert"], description: "降噪/重采样/响度归一化")
                .Add("Vocal Separation", vocalSepOp, dependsOn: ["Audio Preprocess"], description: "Demucs 人声分离");
            var afterAudio = "Vocal Separation";

            var diarizationOp = operatorFactory.CreateDiarizationOperator(config);
            if (diarizationOp is not null)
            {
                builder.Add("Speaker Diarization", diarizationOp,
                    dependsOn: [afterAudio],
                    maxRetries: 1,
                    degradeOnFailure: true,
                    description: "说话人分割标注（失败降级跳过）");
                afterAudio = "Speaker Diarization";
            }

            var alignmentOp = operatorFactory.CreateAlignmentOperator(config);
            if (alignmentOp is not null)
            {
                builder.Add("Force Alignment", alignmentOp,
                    dependsOn: [afterAudio],
                    maxRetries: 1,
                    degradeOnFailure: true,
                    description: "词级强制对齐（失败降级跳过）");
                afterAudio = "Force Alignment";
            }

            builder.Add("Resolve Overlaps", overlapOp, dependsOn: [afterAudio], description: "重叠时间轴消解");
            join.Add("Resolve Overlaps");
        }

        builder.Add("Correction Report", reportOp, dependsOn: join.Count > 0 ? join : null, description: "校正报告（文本/时间轴修正明细）");
        builder.Add("Spell Check", spellCheckOp,
            dependsOn: ["Correction Report"],
            when: _ => runSpellCheck,
            description: "Hunspell 拼写检查（按 --spellcheck 开启）");
        builder.Add("Quality Report", qualityReportOp, dependsOn: ["Spell Check"], description: "质量报告收尾");
        return builder.Build();
    }

    private static CorrectionStrategy ParseStrategy(string value) => value.ToLowerInvariant() switch
    {
        "timeline-only" => CorrectionStrategy.TimelineOnly,
        "text-only" => CorrectionStrategy.TextOnly,
        "both" => CorrectionStrategy.Both,
        _ => throw new ArgumentException($"Unsupported correction strategy: {value}")
    };

    private static void Validate(CorrectSettings settings, CorrectionStrategy strategy)
    {
        if ((strategy is CorrectionStrategy.TextOnly or CorrectionStrategy.Both) && settings.ScriptFile is null)
            throw new ArgumentException("--script is required for the selected correction strategy.");
        if (settings.FuzzyThreshold is <= 0 or >= 1)
            throw new ArgumentException("--fuzzy-threshold must be between 0 and 1.");
        if (settings.MaxDrift < 0)
            throw new ArgumentException("--max-drift must be non-negative.");
    }

    private static readonly HashSet<string> MediaFileExtensions =
    [
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".ts", ".mts", ".webm", ".flv",
        ".m2ts", ".mpeg", ".mpg", ".dv", ".rmvb", ".rm", ".asf", ".vob", ".ogv", ".mxf"
    ];

    private static bool IsMediaFile(string path) =>
        MediaFileExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());
}
