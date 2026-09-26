using Centurion.Cli.Commands.Settings;
using Centurion.Abstractions;
using Centurion.Abstractions.Pipeline;
using Centurion.Core.Capabilities.Infrastructure;using Centurion.Core.Capabilities.Infrastructure.Asr;using Centurion.Core.Workflow.Factories;using Centurion.Models.Ass;
using Centurion.Models.Workflow;
using Centurion.Core.Workflow.Pipeline;using Centurion.Core.Workflow.Pipeline.Operators;using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;
using Centurion.Abstractions.Utils;
using Centurion.Core.Utils.Serialization;
using Centurion.Models.Console;
namespace Centurion.Cli.Commands;

/// <summary>
/// <c>asr</c> command: transcribe, diarize, split and align audio/video media into subtitles.
/// </summary>
public sealed class SpawnCommand(
    ITempDirectoryManager tempManager,
    SubtitleTrackCheckerOperator subtitleTrackCheckerOp,
    FFmpegConvertOperator ffmpegOp,
    AudioPreprocessOperator audioPreprocessOp,
    VocalSeparationOperator vocalSepOp,
    VoiceActivityFilterOperator vadOp,
    PipelineOperatorFactory operatorFactory,
    TextPreprocessingOperator textCleaningOp,
    QualityReportOperator qualityReportOp,
    PipelineExecutor pipelineExecutor,
    IServiceProvider serviceProvider,
    ILogger<SpawnCommand> logger, ICenturionDocumentStore store)
    : AsyncCommand<SpawnSettings>
{
    /// <summary>
    /// Runs the auto-subtitle pipeline: assembles and runs it, then writes the ASS subtitle file.
    /// </summary>
    /// <param name="context">The Spectre command context.</param>
    /// <param name="settings">The asr (spawn) command settings.</param>
    /// <param name="ct">The cancellation token.</param>
    protected override async Task<int> ExecuteAsync(CommandContext context, SpawnSettings settings, CancellationToken ct)
    {
        try
        {
            var inputPath = settings.InputFile.FullName;
            // -o 现在写入的是 IR 中间文件；它是结构化字幕交换格式，不直接产出最终字幕 
            var outputPath = settings.OutputFile?.FullName ?? CenturionFileIO.DefaultOutputPath(inputPath, "asr");
            var intermediatePath = outputPath;

            // 云端 ASR 提前校验：缺 API 密钥则在转换/转录前失败
            if (AsrEndpointParser.IsCloud(settings.Transcriber) && string.IsNullOrWhiteSpace(settings.AsrApiKey))
                throw new ArgumentException(
                    $"Cloud ASR provider '{settings.Transcriber}' requires an API key. Provide --asr-api-key <KEY>.");

            var extension = Path.GetExtension(inputPath).ToLowerInvariant();
            if (!MediaFileExtensions.VideoAndAudio.Contains(extension))
                throw new ArgumentException($"Unsupported media file type: {extension}");

            // Build workflow configuration
            var config = new WorkflowConfig
            {
                CommandName = "asr",
                InputFilePath = inputPath,
                OutputFilePath = intermediatePath,
                Language = settings.Language,
                NumSpeakers = settings.NumSpeakers,
                DiarizationBackend = settings.DiarizationBackend is not null
                    ? settings.DiarizationBackend
                    : settings.Diarize ? "crispasr" : "none",
                KaraokeMode = settings.Karaoke,
                ShowSpeakerLabels = settings.ShowSpeakerLabels,
                CacheDirectory = "./cache",

                TranscriberEngine = settings.Transcriber,
                TranscriberModel = settings.TranscriberModel,
                InitialPrompt = settings.InitialPrompt,
                AsrProvider = settings.AsrProvider,
                AsrApiKey = settings.AsrApiKey,
                AsrBaseUrl = settings.AsrBaseUrl,
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

                SplitStrategy = settings.Splitter,
                MaxSentenceLength = settings.MaxLength,
                TargetSentenceLength = settings.TargetLength,
                SpreadRange = settings.SpreadRange,
                ChunkGranularity = settings.ChunkGranularity,
                MergeGapSeconds = 1.5,
                EnablePunctuationRewrite = true,
                SplitterModel = settings.SplitterModel,
                SplitterApiKey = settings.SplitterApiKey,
                SplitterProvider = settings.LlmProvider,
                SplitterBaseUrl = settings.LlmBaseUrl,

                EnableAlignment = settings.EnableAlignment,
                AlignmentModel = settings.AlignmentModel,
                AlignmentChunkGapSeconds = settings.AlignmentChunkGapSeconds,
                AlignmentMaxChunkSeconds = settings.AlignmentMaxChunkSeconds,

            };

            var workflowContext = new SubtitleWorkflowContext(config);

            // ─── 组装 ASR DAG：节点=算子、边=数据依赖；条件节点按配置跳过，diarization 失败可降级 ───
            var dag = BuildAsrDag(
                subtitleTrackCheckerOp, ffmpegOp, audioPreprocessOp, vocalSepOp, vadOp,
                operatorFactory, textCleaningOp, qualityReportOp, config);

            // Create pipeline temp directory after all configured strategies resolve.
            await using var tempDir = await tempManager.CreateTempDirectoryAsync("pipeline_");
            workflowContext.State.PipelineTempDirectory = tempDir.Path;

            // --dry-run：预览 DAG / 模型 / 成本，不执行
            if (settings.DryRun)
                return await DryRunHelper.PreviewAsync(dag, config, serviceProvider, settings.Json, ct);

            // Execute the DAG pipeline（就绪节点并行；条件跳过、重试、超时、降级由执行器统一处理）
            var stepResults = await pipelineExecutor.ExecuteAsync(dag, workflowContext, ct);
            var skipped = stepResults.Where(r => r.Status == PipelineStepStatus.Skipped).Select(r => r.Name).ToList();
            if (skipped.Count > 0)
                logger.LogInformation("Skipped {Count} conditional step(s): {Names}", skipped.Count, string.Join(", ", skipped));
            var degraded = stepResults.Where(r => r.Status == PipelineStepStatus.Degraded).Select(r => r.Name).ToList();
            if (degraded.Count > 0)
                logger.LogWarning("Degraded {Count} step(s) after retries: {Names}", degraded.Count, string.Join(", ", degraded));

            // 保存为 Centurion 中间文件（含词级时间戳/说话人/各阶段句子等全部详细信息）
            var outDoc = CenturionDocumentBuilder.Create(workflowContext, "asr", intermediatePath);
            await store.SaveAsync(outDoc, intermediatePath, ct);

            ConsoleServices.Output.WriteSuccess(ConsoleServices.T("Subtitle generation completed"));
            ConsoleServices.Output.WriteInfo(ConsoleServices.T("Intermediate file: {0}", intermediatePath));
            ConsoleServices.Output.WriteInfo(ConsoleServices.T("Build subtitles with: {0}", "Centurion build <file>.centurion.json"));
            if (settings.Json)
            {
                JsonOutput.Write(new
                {
                    command = "asr",
                    status = "ok",
                    input = inputPath,
                    output = intermediatePath,
                    steps = workflowContext.State.StepTimings?.Select(kv => new { name = kv.Key, elapsedSeconds = kv.Value.TotalSeconds })
                });
            }
            return ExitCodes.Success;
        }
        catch (Exception ex)
        {
            CliErrorPrinter.Print(logger, ex, "Pipeline execution failed.");
            return ExitCodes.Failure;
        }
    }

    /// <summary>
    /// Assembles the ASR DAG (single source of truth shared with the pipeline graph command):
    /// conditional nodes are skipped per config (vocal separation/diarization/alignment/cleaning);
    /// diarization and alignment degrade on failure (skip after retries), never stalling the whole task.
    /// </summary>
    internal static PipelineDag BuildAsrDag(
        SubtitleTrackCheckerOperator trackChecker,
        FFmpegConvertOperator ffmpegOp,
        AudioPreprocessOperator audioPreprocessOp,
        VocalSeparationOperator vocalSepOp,
        VoiceActivityFilterOperator vadOp,
        PipelineOperatorFactory operatorFactory,
        TextPreprocessingOperator textCleaningOp,
        QualityReportOperator qualityReportOp,
        Centurion.Models.Workflow.WorkflowConfig config)
    {
        var transcribeOp = operatorFactory.CreateTranscribeOperator(config);
        var diarizationOp = operatorFactory.CreateDiarizationOperator(config);
        var splitOp = operatorFactory.CreateSentenceSplitOperator(config);
        var alignmentOp = operatorFactory.CreateAlignmentOperator(config);

        var builder = PipelineDag.CreateBuilder();
        builder
            .Add("Track Check", trackChecker, description: "Inspect input media tracks and format")
            .Add("FFmpeg Convert", ffmpegOp, dependsOn: ["Track Check"], description: "Resample/transcode to a unified audio")
            .Add("Audio Preprocess", audioPreprocessOp, dependsOn: ["FFmpeg Convert"], description: "Noise reduction/resample/loudness normalization")
            .Add("Voice Activity Filter", vadOp,
                dependsOn: ["Audio Preprocess"],
                when: c => c.Config.EnableVadFilter,
                description: "VAD filter: aggregate speech, drop instrumental/silence segments")
            .Add("Vocal Separation", vocalSepOp,
                dependsOn: ["Audio Preprocess", "Voice Activity Filter"],
                when: c => c.Config.VocalSeparation,
                description: "Demucs vocal separation (enabled by config; skipped when off)")
            .Add("Transcribe", transcribeOp,
                dependsOn: ["Vocal Separation"],
                maxRetries: 1,
                description: "ASR transcription (auto-retry once on failure)");

        var afterTranscribe = "Transcribe";
        if (diarizationOp is not null)
        {
            builder.Add("Speaker Diarization", diarizationOp,
                dependsOn: [afterTranscribe],
                when: c => !string.Equals(c.Config.DiarizationBackend, "none", StringComparison.OrdinalIgnoreCase),
                maxRetries: 1,
                degradeOnFailure: true,
                description: "Speaker diarization (degrade-skip on failure, non-fatal)");
            afterTranscribe = "Speaker Diarization";
        }

        builder.Add("Sentence Splitting", splitOp,
            dependsOn: [afterTranscribe],
            description: "Sentence splitting (merge/split)");

        var afterSplit = "Sentence Splitting";
        builder.Add("Text Cleaning", textCleaningOp,
            dependsOn: [afterSplit],
            when: c => c.Config.EnableTextCleaning,
            description: "Text cleaning before alignment (enabled by config)");
        afterSplit = "Text Cleaning";

        if (alignmentOp is not null)
        {
            builder.Add("Force Alignment", alignmentOp,
                dependsOn: [afterSplit],
                when: c => c.Config.EnableAlignment,
                maxRetries: 1,
                degradeOnFailure: true,
                description: "Word-level forced alignment (degrade-skip on failure, non-fatal)");
            afterSplit = "Force Alignment";
        }

        builder.Add("Quality Report", qualityReportOp,
            dependsOn: [afterSplit],
            description: "Quality report wrap-up");
        return builder.Build();
    }

}
