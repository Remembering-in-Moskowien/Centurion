using Centurion.Abstractions;
using Centurion.Abstractions.Pipeline;
using Centurion.Abstractions.Utils;
using Centurion.Cli.Commands.Settings;
using Centurion.Core.Workflow.Factories;using Centurion.Core.Capabilities.Infrastructure;using Centurion.Core.Capabilities.Infrastructure.Ocr;using Centurion.Core.Workflow.Pipeline;using Centurion.Core.Workflow.Pipeline.Operators;using Centurion.Models.Workflow;
using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;
using Centurion.Core.Utils.Serialization;
using Centurion.Models.Console;
namespace Centurion.Cli.Commands;

/// <summary><c>ocr</c> 命令：从视频或图片提取字幕文字，写入中间文件。</summary>
public sealed class OcrCommand(
    OcrClient ocrClient,
    RapidOcrEngine rapidOcrEngine,
    OcrExtractOperator ocrExtractOp,
    ITempDirectoryManager tempManager,
    PipelineOperatorFactory operatorFactory,
    TextPreprocessingOperator textCleaningOp,
    QualityReportOperator qualityReportOp,
    PipelineExecutor pipelineExecutor,
    ILogger<OcrCommand> logger, ICenturionDocumentStore store) : AsyncCommand<OcrSettings>
{
    /// <summary>组装并运行 OCR、分句与文本清理管线。</summary>
    protected override async Task<int> ExecuteAsync(CommandContext context, OcrSettings settings, CancellationToken ct)
    {
        try
        {
            var inputPath = settings.InputFile.FullName;
            if (!File.Exists(inputPath))
                throw new FileNotFoundException($"Input file not found: {inputPath}", inputPath);

            var extension = Path.GetExtension(inputPath).ToLowerInvariant();
            if (!MediaFileExtensions.OcrInputs.Contains(extension))
                throw new ArgumentException($"Unsupported media or image file type: {extension}");

            var backend = OcrExtractOperator.ParseBackend(settings.OcrBackend);
            if (backend == OcrBackend.Zhipu && string.IsNullOrWhiteSpace(settings.OcrApiKey))
                throw new ArgumentException(
                    "OCR with zhipu backend requires a GLM-OCR API key. Provide --ocr-api-key <KEY>, or use --ocr-backend ollama/llamacpp for local inference.");
            if (backend == OcrBackend.RapidOcr)
            {
                if (!await rapidOcrEngine.IsAvailableAsync(ct))
                    throw new InvalidOperationException(
                        "RapidOCR engine is not available: model download failed or ONNX runtime missing.");
            }
            else if (backend != OcrBackend.Zhipu && !await ocrClient.ProbeAsync(backend, settings.OcrBaseUrl, ct))
                throw new InvalidOperationException(
                    "Local OCR backend not reachable. Start 'ollama serve' or your llama-server, then retry.");

            var intermediatePath = settings.OutputFile?.FullName
                ?? CenturionFileIO.DefaultOutputPath(inputPath, "ocr");
            var config = new WorkflowConfig
            {
                CommandName = "ocr",
                InputFilePath = inputPath,
                OutputFilePath = intermediatePath,
                Language = settings.Language,
                CacheDirectory = "./cache",
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
                EnableAlignment = false,
                OcrIntervalSeconds = settings.OcrIntervalSeconds > 0 ? settings.OcrIntervalSeconds : 2.0,
                OcrVideoSubFinderPath = settings.OcrVideoSubFinderPath,
                OcrBackend = settings.OcrBackend,
                OcrModel = settings.OcrModel,
                OcrApiKey = settings.OcrApiKey,
                OcrBaseUrl = settings.OcrBaseUrl
            };
            var workflowContext = new SubtitleWorkflowContext(config);
            var operators = new List<IPipelineOperator>
            {
                ocrExtractOp,
                operatorFactory.CreateSentenceSplitOperator(config),
                textCleaningOp,
                qualityReportOp
            };

            await using var tempDir = await tempManager.CreateTempDirectoryAsync("ocr_");
            workflowContext.State.PipelineTempDirectory = tempDir.Path;
            await pipelineExecutor.ExecuteAsync(operators, workflowContext, ct);
            var outDoc = CenturionDocumentBuilder.Create(workflowContext, "ocr", intermediatePath);
            await store.SaveAsync(outDoc, intermediatePath, ct);

            ConsoleServices.Output.WriteSuccess(ConsoleServices.T("OCR completed"));
            ConsoleServices.Output.WriteInfo(ConsoleServices.T("Intermediate file: {0}", intermediatePath));
            ConsoleServices.Output.WriteInfo(ConsoleServices.T("Build subtitles with: {0}", "Centurion build <file>.centurion.json"));
            return 0;
        }
        catch (Exception ex)
        {
            FailLogGate.Log(logger, ex, "OCR pipeline execution failed.");
            return 1;
        }
    }
}