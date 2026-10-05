using Centurion.Abstractions;
using Centurion.Abstractions.Pipeline;
using Centurion.Abstractions.Utils;
using Centurion.Cli.Commands.Settings;
using Centurion.Core.Workflow.Factories;
using Centurion.Core.Capabilities.Infrastructure;
using Centurion.Core.Capabilities.Infrastructure.Ocr;
using Centurion.Core.Workflow.Pipeline;
using Centurion.Core.Workflow.Pipeline.Operators;
using Centurion.Models.Workflow;
using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;
using Centurion.Core.Utils.Serialization;
using Centurion.Models.Console;
namespace Centurion.Cli.Commands;

/// <summary><c>ocr</c> command: extracts subtitle text from video or images into an intermediate file.</summary>
public sealed class OcrCommand(
    OcrClient ocrClient,
    RapidOcrEngine rapidOcrEngine,
    OcrExtractOperator ocrExtractOp,
    ITempDirectoryManager tempManager,
    PipelineOperatorFactory operatorFactory,
    TextPreprocessingOperator textCleaningOp,
    QualityReportOperator qualityReportOp,
    PipelineExecutor pipelineExecutor,
    IServiceProvider serviceProvider,
    ILogger<OcrCommand> logger, ICenturionDocumentStore store) : AsyncCommand<OcrSettings>
{
    /// <summary>Assembles and runs the OCR, splitting and text-cleaning pipeline.</summary>
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
                if (!await rapidOcrEngine.EnsureAvailableAsync(ct))
                    throw new InvalidOperationException(
                        "RapidOCR engine is not available: automatic model download from ModelScope failed. Check network connectivity and retry.");
            }
            else if (backend != OcrBackend.Zhipu && !await ocrClient.ProbeAsync(backend, settings.OcrBaseUrl, ct))
                throw new InvalidOperationException(
                    "Local OCR backend not reachable. Start 'ollama serve' or your llama-server, then retry.");
            ValidateRoi(settings.OcrRoiTop, "--ocr-roi-top");
            ValidateRoi(settings.OcrRoiBottom, "--ocr-roi-bottom");
            ValidateRoi(settings.OcrRoiLeft, "--ocr-roi-left");
            ValidateRoi(settings.OcrRoiRight, "--ocr-roi-right");


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
                OcrBaseUrl = settings.OcrBaseUrl,
                OcrRoiTop = settings.OcrRoiTop,
                OcrRoiBottom = settings.OcrRoiBottom,
                OcrRoiLeft = settings.OcrRoiLeft,
                OcrRoiRight = settings.OcrRoiRight
            };
            var workflowContext = new SubtitleWorkflowContext(config);

            // OCR DAG: extract and recognize frames, split and clean text, then generate the quality report; pipeline-graph uses the same assembly.
            var dag = BuildOcrDag(ocrExtractOp, operatorFactory, textCleaningOp, qualityReportOp, config);

            await using var tempDir = await tempManager.CreateTempDirectoryAsync("ocr_");
            workflowContext.State.PipelineTempDirectory = tempDir.Path;
            // --dry-run previews the DAG, models, and costs without running operators.
            if (settings.DryRun)
                return await DryRunHelper.PreviewAsync(dag, config, serviceProvider, settings.Json, ct);

            await pipelineExecutor.ExecuteAsync(dag, workflowContext, ct);
            var outDoc = CenturionDocumentBuilder.Create(workflowContext, "ocr", intermediatePath);
            await store.SaveAsync(outDoc, intermediatePath, ct);

            ConsoleServices.Output.WriteSuccess(ConsoleServices.T("OCR completed"));
            ConsoleServices.Output.WriteInfo(ConsoleServices.T("Intermediate file: {0}", intermediatePath));
            ConsoleServices.Output.WriteInfo(ConsoleServices.T("Build subtitles with: {0}", "Centurion build <file>.centurion.json"));
            if (settings.Json)
            {
                JsonOutput.Write(new
                {
                    command = "ocr",
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
            CliErrorPrinter.Print(logger, ex, "OCR pipeline execution failed.");
            return ExitCodes.Failure;
        }
    }

    /// <summary>
    /// Assembles the OCR DAG (single source of truth shared with the pipeline graph command):
    /// frame OCR → sentence splitting → text cleaning → quality report.
    /// </summary>
    internal static PipelineDag BuildOcrDag(
        OcrExtractOperator ocrExtractOp,
        PipelineOperatorFactory operatorFactory,
        TextPreprocessingOperator textCleaningOp,
        QualityReportOperator qualityReportOp,
        Centurion.Models.Workflow.WorkflowConfig config)
    {
        var splitOp = operatorFactory.CreateSentenceSplitOperator(config);
        var builder = PipelineDag.CreateBuilder();
        builder
            .Add("OCR Extract", ocrExtractOp, description: "VSF/FFmpeg frame extraction + RapidOCR/LLM subtitle recognition")
            .Add("Sentence Splitting", splitOp, dependsOn: ["OCR Extract"], description: "Sentence splitting (merge/split)")
            .Add("Text Cleaning", textCleaningOp, dependsOn: ["Sentence Splitting"], description: "Normalize punctuation/digits/abbreviations")
            .Add("Quality Report", qualityReportOp, dependsOn: ["Text Cleaning"], description: "Quality report wrap-up");
        return builder.Build();
    }

    /// <summary>Validates an ROI ratio in the range 0-1, relative to the video dimensions.</summary>
    /// <param name="value">The optional ratio; null uses the default.</param>
    /// <param name="option">The command-line option name used in error messages.</param>
    private static void ValidateRoi(double? value, string option)
    {
        if (value is < 0 or > 1)
            throw new ArgumentException($"{option} must be between 0 and 1 (video size ratio).");
    }
}
