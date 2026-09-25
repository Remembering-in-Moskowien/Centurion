using Centurion.Models.Console;
using Centurion.Cli.Commands.Settings;
using Centurion.Abstractions;
using Centurion.Abstractions.Pipeline;
using Centurion.Core.Infrastructure;
using Centurion.Models.Ass;
using Centurion.Models.Workflow;
using Centurion.Core.Pipeline;
using Centurion.Core.Pipeline.Operators;
using Centurion.Core.Utils;
using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;
using Centurion.Abstractions.Utils;

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
    DiarizationOperator diarizationOp,
    AlignmentOperator alignmentOp,
    OverlapResolutionOperator overlapOp,
    CorrectionReportOperator reportOp,
    QualityReportOperator qualityReportOp,
    PipelineExecutor pipelineExecutor,
    ILogger<CorrectCommand> logger) : AsyncCommand<CorrectSettings>
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

            var workflowContext = await CenturionFileIO.LoadAsync(inputPath, cancellationToken);
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

            var operators = new List<IPipelineOperator>();
            if (strategy is CorrectionStrategy.TextOnly or CorrectionStrategy.Both)
            {
                operators.Add(scriptLoaderOp);
                operators.Add(textCorrectorOp);
            }

            if (needsAudio)
            {
                operators.Add(ffmpegOp);
                operators.Add(audioPreprocessOp);
                operators.Add(vocalSepOp);
                operators.Add(diarizationOp);
                operators.Add(alignmentOp);
                operators.Add(overlapOp);
            }

            if (settings.SpellCheck)
                operators.Add(spellCheckOp);

            operators.Add(reportOp);
            operators.Add(qualityReportOp);
            await pipelineExecutor.ExecuteAsync(operators, workflowContext, cancellationToken);

            // 保存校正后的中间文件（时间轴/文本修正全部写入状态）
            await CenturionFileIO.SaveAsync(workflowContext, outputPath, "correct", cancellationToken);

            ConsoleServices.Output.WriteSuccess(ConsoleServices.T("Correction completed: {0}", outputPath));
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
