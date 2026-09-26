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
/// <c>asr</c> 命令：从音视频媒体自动转录、说话人分割、分句与对齐，生成字幕。
/// </summary>
public sealed class SpawnCommand(
    ITempDirectoryManager tempManager,
    SubtitleTrackCheckerOperator subtitleTrackCheckerOp,
    FFmpegConvertOperator ffmpegOp,
    AudioPreprocessOperator audioPreprocessOp,
    VocalSeparationOperator vocalSepOp,
    PipelineOperatorFactory operatorFactory,
    TextPreprocessingOperator textCleaningOp,
    QualityReportOperator qualityReportOp,
    PipelineExecutor pipelineExecutor,
    ILogger<SpawnCommand> logger, ICenturionDocumentStore store)
    : AsyncCommand<SpawnSettings>
{
    /// <summary>
    /// 执行自动字幕生成流程：组装并运行管道，写出 ASS 字幕文件。
    /// </summary>
    /// <param name="context">Spectre 命令上下文。</param>
    /// <param name="settings">spawn 命令选项。</param>
    /// <param name="ct">取消令牌。</param>
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

            // ─── Dynamically build the ASR operator pipeline ───
            var operators = new List<IPipelineOperator>();
            operators.Add(subtitleTrackCheckerOp);
            operators.Add(ffmpegOp);
            operators.Add(audioPreprocessOp);
            operators.Add(vocalSepOp);
            operators.Add(operatorFactory.CreateTranscribeOperator(config));
            var diarizationOperator = operatorFactory.CreateDiarizationOperator(config);
            if (diarizationOperator is not null)
                operators.Add(diarizationOperator);
            operators.Add(operatorFactory.CreateSentenceSplitOperator(config));
            operators.Add(textCleaningOp);
            var alignmentOperator = operatorFactory.CreateAlignmentOperator(config);
            if (alignmentOperator is not null)
                operators.Add(alignmentOperator);
            operators.Add(qualityReportOp);

            // Create pipeline temp directory after all configured strategies resolve.
            await using var tempDir = await tempManager.CreateTempDirectoryAsync("pipeline_");
            workflowContext.State.PipelineTempDirectory = tempDir.Path;

            // Execute the dynamic pipeline
            await pipelineExecutor.ExecuteAsync(operators, workflowContext, ct);

            // 保存为 Centurion 中间文件（含词级时间戳/说话人/各阶段句子等全部详细信息）
            var outDoc = CenturionDocumentBuilder.Create(workflowContext, "asr", intermediatePath);
            await store.SaveAsync(outDoc, intermediatePath, ct);

            ConsoleServices.Output.WriteSuccess(ConsoleServices.T("Subtitle generation completed"));
            ConsoleServices.Output.WriteInfo(ConsoleServices.T("Intermediate file: {0}", intermediatePath));
            ConsoleServices.Output.WriteInfo(ConsoleServices.T("Build subtitles with: {0}", "Centurion build <file>.centurion.json"));
            return 0;
        }
        catch (Exception ex)
        {
            FailLogGate.Log(logger, ex, "Pipeline execution failed.");
            return 1;
        }
    }

}
