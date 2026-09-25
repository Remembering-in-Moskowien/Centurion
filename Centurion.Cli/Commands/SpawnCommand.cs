using Centurion.Models.Console;
using Centurion.Cli.Commands.Settings;
using Centurion.Abstractions;
using Centurion.Abstractions.Pipeline;
using Centurion.Core.Infrastructure;
using Centurion.Core.Asr;
using Centurion.Core.Ocr;
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
/// <c>spawn</c> 命令：从音视频媒体自动转录、说话人分割、分句与对齐，生成字幕。
/// </summary>
public sealed class SpawnCommand(
    OcrClient ocrClient,
    OcrExtractOperator ocrExtractOp,
    ITempDirectoryManager tempManager,
    SubtitleTrackCheckerOperator subtitleTrackCheckerOp,
    FFmpegConvertOperator ffmpegOp,
    AudioPreprocessOperator audioPreprocessOp,
    VocalSeparationOperator vocalSepOp,
    TranscribeOperator transcribeOp,
    DiarizationOperator diarizationOp,
    SentenceSplitOperator splitOp,
    TextPreprocessingOperator textCleaningOp,
    AlignmentOperator alignmentOp,
    QualityReportOperator qualityReportOp,
    PipelineExecutor pipelineExecutor,
    ILogger<SpawnCommand> logger)
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
            // -o 为目标 ASS 字幕路径（默认 <input>.spawn.ass）；中间文件与之同名 .centurion.json 一并保留
            var outputPath = settings.OutputFile?.FullName ?? Path.ChangeExtension(inputPath, null) + ".spawn.ass";
            var intermediatePath = Path.ChangeExtension(outputPath, CenturionFileIO.Extension);

            var isOcrMode = settings.Mode.Equals("ocr", StringComparison.OrdinalIgnoreCase);
            if (!isOcrMode && !settings.Mode.Equals("asr", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Unsupported spawn mode '{settings.Mode}'. Use 'asr' or 'ocr'.");

            // 云端 ASR 提前校验：缺 API 密钥则在转换/转录前失败
            if (!isOcrMode && AsrEndpointParser.IsCloud(settings.Transcriber) && string.IsNullOrWhiteSpace(settings.AsrApiKey))
                throw new ArgumentException(
                    $"Cloud ASR provider '{settings.Transcriber}' requires an API key. Provide --asr-api-key <KEY>.");

            // OCR 模式提前校验：云端缺 API 密钥则在抽帧前失败；本地后端探测服务在线
            if (isOcrMode)
            {
                var ocrBackend = OcrExtractOperator.ParseBackend(settings.OcrBackend);
                if (ocrBackend == OcrBackend.Zhipu && string.IsNullOrWhiteSpace(settings.OcrApiKey))
                    throw new ArgumentException(
                        "OCR mode with zhipu backend requires a GLM-OCR API key. Provide --ocr-api-key <KEY>, or use --ocr-backend ollama/llamacpp for local inference.");
                if (ocrBackend != OcrBackend.Zhipu &&
                    !await ocrClient.ProbeAsync(ocrBackend, settings.OcrBaseUrl, ct))
                    throw new InvalidOperationException(
                        "Local OCR backend not reachable. Start the service first: 'ollama serve' (Ollama) or your llama-server, then retry.");
            }

            // 校验媒体扩展名：OCR 模式额外允许图片（png/jpg 等）
            var extension = Path.GetExtension(inputPath).ToLowerInvariant();
            var allowedExtensions = isOcrMode
                ? MediaFileExtensions.Union(OcrImageExtensions)
                : MediaFileExtensions;
            if (!allowedExtensions.Contains(extension))
                throw new ArgumentException($"Unsupported media file type: {extension}");

            // Build workflow configuration
            var config = new WorkflowConfig
            {
                CommandName = "spawn",
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

                EnableAlignment = isOcrMode ? false : settings.EnableAlignment,
                AlignmentModel = settings.AlignmentModel,
                AlignmentChunkGapSeconds = settings.AlignmentChunkGapSeconds,
                AlignmentMaxChunkSeconds = settings.AlignmentMaxChunkSeconds,

                // OCR 模式（GLM-OCR）
                OcrIntervalSeconds = settings.OcrIntervalSeconds > 0 ? settings.OcrIntervalSeconds : 2.0,
                OcrBackend = settings.OcrBackend,
                OcrModel = settings.OcrModel,
                OcrApiKey = settings.OcrApiKey,
                OcrBaseUrl = settings.OcrBaseUrl
            };

            var workflowContext = new SubtitleWorkflowContext(config);

            // Create pipeline temp directory
            await using var tempDir = await tempManager.CreateTempDirectoryAsync("pipeline_");
            workflowContext.State.PipelineTempDirectory = tempDir.Path;

            // ─── Dynamically build the operator pipeline ───
            // asr（默认）：完整语音识别管线；ocr：抽帧 + GLM-OCR，跳过音频/说话人/对齐
            var operators = isOcrMode
                ? new List<IPipelineOperator>
                {
                    ocrExtractOp,
                    splitOp,
                    textCleaningOp,
                    qualityReportOp
                }
                : new List<IPipelineOperator>
                {
                    subtitleTrackCheckerOp,
                    ffmpegOp,
                    audioPreprocessOp,
                    vocalSepOp,
                    transcribeOp,
                    diarizationOp,
                    splitOp,
                    textCleaningOp,
                    alignmentOp,
                    qualityReportOp
                };

            // Execute the dynamic pipeline
            await pipelineExecutor.ExecuteAsync(operators, workflowContext, ct);

            // 保存为 Centurion 中间文件（含词级时间戳/说话人/各阶段句子等全部详细信息）
            await CenturionFileIO.SaveAsync(workflowContext, intermediatePath, "spawn", ct);

            // 2) 渲染 ASS 字幕到 -o（与 help 声明一致：Output ASS subtitle file）
            var assContent = AssSubBuilder.FromWorkflow(workflowContext).Build().ToString();
            await File.WriteAllTextAsync(outputPath, assContent, ct);

            ConsoleServices.Output.WriteSuccess(ConsoleServices.T("Subtitle generation completed: {0}", outputPath));
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

    private static readonly HashSet<string> OcrImageExtensions =
        [".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp"];

    private static readonly HashSet<string> MediaFileExtensions =
    [
        ".mp3", ".wma", ".wav", ".flac", ".aac", ".ogg", ".ape", ".m4a", ".mka",
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".ts", ".mts", ".webm", ".flv",
        ".m2ts", ".mpeg", ".mpg", ".dv", ".rmvb", ".rm", ".asf", ".vob", ".ogv", ".mxf"
    ];
}
