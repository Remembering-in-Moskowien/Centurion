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
    VoiceActivityFilterOperator vadOp,
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
    /// Runs the script-alignment flow: transcribe, diarize, map and align against the script, writing a timestamped IR.
    /// </summary>
    /// <param name="context">The Spectre command context.</param>
    /// <param name="settings">The from-script command settings.</param>
    /// <param name="ct">The cancellation token.</param>
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
                EnableVadFilter = !settings.DisableVadFilter,
                VadEnergyThresholdRatio = settings.VadEnergyThresholdRatio,
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
                subtitleTrackCheckerOp, ffmpegOp, audioPreprocessOp, vocalSepOp, vadOp,
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
    /// Assembles the from-script DAG (single source of truth shared with the pipeline graph command):
    /// track check → FFmpeg convert → audio preprocess → vocal separation → transcription
    /// → script load → cleaning → timeline mapping → forced alignment → quality report;
    /// diarization/alignment join conditionally on availability and config.
    /// </summary>
    internal static PipelineDag BuildFromScriptDag(
        SubtitleTrackCheckerOperator subtitleTrackCheckerOp,
        FFmpegConvertOperator ffmpegOp,
        AudioPreprocessOperator audioPreprocessOp,
        VocalSeparationOperator vocalSepOp,
        VoiceActivityFilterOperator vadOp,
        PipelineOperatorFactory operatorFactory,
        ScriptLoaderOperator scriptLoaderOp,
        TextPreprocessingOperator textCleaningOp,
        ScriptTimelineMapperOperator mapperOp,
        QualityReportOperator qualityReportOp,
        Centurion.Models.Workflow.WorkflowConfig config)
    {
        var builder = PipelineDag.CreateBuilder();
        builder
            .Add("Track Check", subtitleTrackCheckerOp, description: "Inspect input media tracks and format")
            .Add("FFmpeg Convert", ffmpegOp, dependsOn: ["Track Check"], description: "Resample/transcode to a unified audio")
            .Add("Audio Preprocess", audioPreprocessOp, dependsOn: ["FFmpeg Convert"], description: "Noise reduction/resample/loudness normalization")
            .Add("Voice Activity Filter", vadOp, dependsOn: ["Audio Preprocess"],
                when: c => c.Config.EnableVadFilter,
                description: "VAD filter: aggregate speech, drop instrumental/silence segments")
            .Add("Vocal Separation", vocalSepOp, dependsOn: ["Audio Preprocess", "Voice Activity Filter"], description: "Demucs vocal separation")
            .Add("Transcribe", operatorFactory.CreateTranscribeOperator(config),
                dependsOn: ["Vocal Separation"], maxRetries: 1, description: "ASR transcription (auto-retry once on failure)");

        var afterTranscribe = "Transcribe";
        var diarizationOp = operatorFactory.CreateDiarizationOperator(config);
        if (diarizationOp is not null)
        {
            builder.Add("Speaker Diarization", diarizationOp,
                dependsOn: [afterTranscribe],
                maxRetries: 1,
                degradeOnFailure: true,
                description: "Speaker diarization (degrade-skip on failure, non-fatal)");
            afterTranscribe = "Speaker Diarization";
        }

        builder.Add("Script Load", scriptLoaderOp, dependsOn: [afterTranscribe], description: "Load the script/reference")
            .Add("Text Cleaning", textCleaningOp, dependsOn: ["Script Load"], description: "Normalize punctuation/digits/abbreviations")
            .Add("Timeline Mapping", mapperOp, dependsOn: ["Text Cleaning"], description: "Map the script onto the transcription timeline");

        var afterMapping = "Timeline Mapping";
        var alignmentOp = operatorFactory.CreateAlignmentOperator(config);
        if (alignmentOp is not null)
        {
            builder.Add("Force Alignment", alignmentOp,
                dependsOn: [afterMapping],
                maxRetries: 1,
                degradeOnFailure: true,
                description: "Word-level forced alignment (degrade-skip on failure, non-fatal)");
            afterMapping = "Force Alignment";
        }

        builder.Add("Quality Report", qualityReportOp, dependsOn: [afterMapping], description: "Quality report wrap-up");
        return builder.Build();
    }
}
