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
/// Script timing: align a plain-text script to the media and generate timed subtitles.
/// </summary>
public sealed class FromScriptCommand(
    ITempDirectoryManager tempManager,
    SubtitleTrackCheckerOperator subtitleTrackCheckerOp,
    FFmpegConvertOperator ffmpegOp,
    AudioPreprocessOperator audioPreprocessOp,
    VocalSeparationOperator vocalSepOp,
    PipelineOperatorFactory operatorFactory,
    ScriptLoaderOperator scriptLoaderOp,
    TextPreprocessingOperator textCleaningOp,
    ScriptTimelineMapperOperator mapperOp,
    QualityReportOperator qualityReportOp,
    PipelineExecutor pipelineExecutor,
    IServiceProvider serviceProvider,
    ILogger<FromScriptCommand> logger, ICenturionDocumentStore store) : AsyncCommand<FromScriptSettings>
{
    /// <summary>
    /// 执行脚本对齐流程：转录、说话人分割、脚本映射与对齐，写出带时间轴的 Centurion 中间文件。
    /// </summary>
    /// <param name="context">Spectre 命令上下文。</param>
    /// <param name="settings">脚本对齐命令选项。</param>
    /// <param name="ct">取消令牌。</param>
    protected override async Task<int> ExecuteAsync(CommandContext context, FromScriptSettings settings, CancellationToken ct)
    {
        try
        {
            var inputPath = settings.InputFile.FullName;
            if (!File.Exists(inputPath))
                throw new FileNotFoundException($"Input media file not found: {inputPath}", inputPath);
            if (!File.Exists(settings.ScriptFile.FullName))
                throw new FileNotFoundException($"Script file not found: {settings.ScriptFile.FullName}", settings.ScriptFile.FullName);

            var outputPath = settings.OutputFile?.FullName ?? CenturionFileIO.DefaultOutputPath(inputPath);
            var config = new WorkflowConfig
            {
                CommandName = "from-script",
                InputFilePath = inputPath,
                OutputFilePath = outputPath,
                ScriptFilePath = settings.ScriptFile.FullName,
                MapperStrategy = "rule",
                SplitStrategy = "nlp",
                Language = settings.Language,
                ShowSpeakerLabels = settings.ShowSpeakerLabels,
                TranscriberEngine = settings.Transcriber,
                TranscriberModel = settings.TranscriberModel,
                AudioPreprocess = new AudioPreprocessConfig
                {
                    EnableResampling = !settings.DisableAudioResampling,
                    EnableHighPass = !settings.DisableAudioHighPass,
                    EnableLoudnessNormalization = !settings.DisableAudioLoudness,
                    EnableNoiseReduction = settings.EnableAudioNoiseReduction,
                    SnrThresholdDb = settings.AudioSnrThresholdDb
                },
                VocalSeparation = settings.VocalSeparation,
                VocalSeparationModel = settings.VocalSeparationModel,
                Device = settings.Device,
                EnableAlignment = settings.EnableAlignment,
                AlignmentModel = settings.AlignmentModel,
                AlignmentChunkGapSeconds = settings.AlignmentChunkGapSeconds,
                AlignmentMaxChunkSeconds = settings.AlignmentMaxChunkSeconds,
                MaxCps = settings.MaxCps,
                MaxCharsPerLine = settings.MaxCharsPerLine,
                CoverageThreshold = settings.CoverageThreshold,
                FillGapWithEllipsis = settings.FillGapWithEllipsis,
                CacheDirectory = "./cache"
            };

            var workflowContext = new SubtitleWorkflowContext(config);

            // from-script DAG：轨道检查→转换→预处理→人声分离→转录→脚本加载→清洗→映射→对齐→质量报告
            var dag = BuildFromScriptDag(
                subtitleTrackCheckerOp, ffmpegOp, audioPreprocessOp, vocalSepOp,
                operatorFactory, scriptLoaderOp, textCleaningOp, mapperOp, qualityReportOp, config);

            await using var tempDir = await tempManager.CreateTempDirectoryAsync("pipeline_");
            workflowContext.State.PipelineTempDirectory = tempDir.Path;
            // --dry-run：预览 DAG / 模型 / 成本，不执行
            if (settings.DryRun)
                return await DryRunHelper.PreviewAsync(dag, config, serviceProvider, settings.Json, ct);

            await pipelineExecutor.ExecuteAsync(dag, workflowContext, ct);

            // 保存为 Centurion 中间文件（含词级时间戳/说话人/脚本映射等全部详细信息）
            var outDoc = CenturionDocumentBuilder.Create(workflowContext, "from-script", outputPath);
            await store.SaveAsync(outDoc, outputPath, ct);

            ConsoleServices.Output.WriteSuccess(ConsoleServices.T("Subtitle generation completed"));
            ConsoleServices.Output.WriteInfo(ConsoleServices.T("Build subtitles with: {0}", "Centurion build <file>.centurion.json"));
            if (settings.Json)
            {
                JsonOutput.Write(new
                {
                    command = "from-script",
                    status = "ok",
                    input = inputPath,
                    output = outputPath,
                    steps = workflowContext.State.StepTimings?.Select(kv => new { name = kv.Key, elapsedSeconds = kv.Value.TotalSeconds })
                });
            }
            return ExitCodes.Success;
        }
        catch (Exception ex)
        {
            CliErrorPrinter.Print(logger, ex, "From-script pipeline execution failed.");
            return ExitCodes.Failure;
        }
    }

    /// <summary>
    /// 组装 from-script DAG（pipeline graph 命令与 from-script 命令共享的单一事实源）：
    /// 轨道检查 → FFmpeg 转换 → 音频预处理 → 人声分离 → 转录 → 脚本加载 → 清洗 →
    /// 时间线映射 → 强制对齐 → 质量报告；说话人分割/对齐按可用性与配置条件接入。
    /// </summary>
    internal static PipelineDag BuildFromScriptDag(
        SubtitleTrackCheckerOperator subtitleTrackCheckerOp,
        FFmpegConvertOperator ffmpegOp,
        AudioPreprocessOperator audioPreprocessOp,
        VocalSeparationOperator vocalSepOp,
        PipelineOperatorFactory operatorFactory,
        ScriptLoaderOperator scriptLoaderOp,
        TextPreprocessingOperator textCleaningOp,
        ScriptTimelineMapperOperator mapperOp,
        QualityReportOperator qualityReportOp,
        Centurion.Models.Workflow.WorkflowConfig config)
    {
        var builder = PipelineDag.CreateBuilder();
        builder
            .Add("Track Check", subtitleTrackCheckerOp, description: "检查输入媒体轨道与格式")
            .Add("FFmpeg Convert", ffmpegOp, dependsOn: ["Track Check"], description: "重采样/转码为统一音频")
            .Add("Audio Preprocess", audioPreprocessOp, dependsOn: ["FFmpeg Convert"], description: "降噪/重采样/响度归一化")
            .Add("Vocal Separation", vocalSepOp, dependsOn: ["Audio Preprocess"], description: "Demucs 人声分离")
            .Add("Transcribe", operatorFactory.CreateTranscribeOperator(config),
                dependsOn: ["Vocal Separation"], maxRetries: 1, description: "ASR 转录（失败自动重试 1 次）");

        var afterTranscribe = "Transcribe";
        var diarizationOp = operatorFactory.CreateDiarizationOperator(config);
        if (diarizationOp is not null)
        {
            builder.Add("Speaker Diarization", diarizationOp,
                dependsOn: [afterTranscribe],
                maxRetries: 1,
                degradeOnFailure: true,
                description: "说话人分割标注（失败降级跳过，不中断）");
            afterTranscribe = "Speaker Diarization";
        }

        builder.Add("Script Load", scriptLoaderOp, dependsOn: [afterTranscribe], description: "加载台本/参考脚本")
            .Add("Text Cleaning", textCleaningOp, dependsOn: ["Script Load"], description: "标点/数字/缩写规范化")
            .Add("Timeline Mapping", mapperOp, dependsOn: ["Text Cleaning"], description: "台本与转录时间线映射");

        var afterMapping = "Timeline Mapping";
        var alignmentOp = operatorFactory.CreateAlignmentOperator(config);
        if (alignmentOp is not null)
        {
            builder.Add("Force Alignment", alignmentOp,
                dependsOn: [afterMapping],
                maxRetries: 1,
                degradeOnFailure: true,
                description: "词级强制对齐（失败降级跳过，不中断）");
            afterMapping = "Force Alignment";
        }

        builder.Add("Quality Report", qualityReportOp, dependsOn: [afterMapping], description: "质量报告收尾");
        return builder.Build();
    }
}
