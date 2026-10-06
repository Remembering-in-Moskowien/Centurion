using Centurion.Cli.Commands.Settings;
using Centurion.Abstractions.Pipeline;
using Centurion.Core.Capabilities.Infrastructure;
using Centurion.Core.Workflow.Pipeline;
using Centurion.Abstractions;
using Centurion.Abstractions.Factories;
using Centurion.Abstractions.Strategy;
using Centurion.Core.Workflow.Pipeline.Operators;
using Centurion.Models.Ass;
using Centurion.Models.Workflow;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;
using Centurion.Abstractions.Utils;
using Centurion.Core.Utils.Parsing;
using Centurion.Core.Utils.Serialization;
using Centurion.Models.Console;
namespace Centurion.Cli.Commands;

/// <summary>
/// Translation: translate a Centurion intermediate file into a target language.
/// Only performs text-level translation alignment — the timeline and word details are kept unchanged.
/// Supports LLM strategy, a glossary file, and a target-language script (1:1 alignment when counts match).
/// </summary>
public sealed class TranslateCommand(
    ITranslationStrategyFactory strategyFactory,
    QualityReportOperator qualityReportOp,
    PipelineExecutor pipelineExecutor,
    IServiceProvider serviceProvider,
    ILogger<TranslateCommand> logger, ICenturionDocumentStore store) : AsyncCommand<TranslateSettings>
{
    /// <summary>
    /// Runs translation: parse existing subtitles → translate text with the chosen
    /// strategy → write target-language (or bilingual) subtitles. Timestamps are kept;
    /// this is text-layer alignment only.
    /// </summary>
    /// <param name="context">The Spectre command context.</param>
    /// <param name="settings">The translate command settings.</param>
    /// <param name="ct">The cancellation token.</param>
    protected override async Task<int> ExecuteAsync(CommandContext context, TranslateSettings settings, CancellationToken ct)
    {
        try
        {
            var inputPath = settings.CenturionFile.FullName;
            if (!File.Exists(inputPath))
                throw new FileNotFoundException($"Centurion intermediate file not found: {inputPath}", inputPath);
            if (string.IsNullOrWhiteSpace(settings.TargetLanguage))
                throw new ArgumentException("--target-language is required.");

            var outputPath = settings.OutputFile?.FullName ??
                             CenturionFileIO.DefaultOutputPath(inputPath, "translated");

            var loadedDoc = await store.LoadAsync(inputPath, ct);
            var workflowContext = new SubtitleWorkflowContext(loadedDoc.Config) { State = loadedDoc.State };

            // Update translation-related configuration fields.
            workflowContext.Config = new WorkflowConfig
            {
                CommandName = "translate",
                InputFilePath = workflowContext.Config.InputFilePath ?? inputPath,
                SubtitleFilePath = workflowContext.Config.SubtitleFilePath ?? inputPath,
                OutputFilePath = outputPath,
                Language = settings.SourceLanguage ?? "auto",
                TargetLanguage = settings.TargetLanguage,
                TranslationStrategy = settings.Strategy,
                TranslationModel = settings.Model,
                TranslationApiKey = settings.ApiKey,
                TranslationProvider = settings.LlmProvider,
                TranslationBaseUrl = settings.LlmBaseUrl,
                GlossaryPath = settings.Glossary?.FullName,
                TargetScriptPath = settings.TargetScript?.FullName,
                Bilingual = settings.Bilingual,
                KaraokeMode = settings.Karaoke,
                ShowSpeakerLabels = workflowContext.Config.ShowSpeakerLabels,
                CacheDirectory = workflowContext.Config.CacheDirectory ?? "./cache"
            };

            var sentences = workflowContext.State.CurrentSentences;

            // 2) Load the glossary and target-language script.
            var glossary = GlossaryLoader.Load(settings.Glossary?.FullName, logger);
            var targetScriptLines = LoadScriptLines(settings.TargetScript?.FullName, logger);

            // 3) Run the translation and quality-report operators in the DAG.
            // The strategy translates batches in parallel and uses 1:1 alignment when script and source line counts match.
            var options = new TranslationOptions
            {
                SourceLanguage = settings.SourceLanguage ?? "auto",
                TargetLanguage = settings.TargetLanguage,
                Glossary = glossary,
                TargetScriptLines = targetScriptLines
            };
            var strategy = strategyFactory.Create(settings.Strategy, new TranslationRequestOptions
            {
                SourceLanguage = settings.SourceLanguage ?? "auto",
                TargetLanguage = settings.TargetLanguage,
                Model = settings.Model,
                BeamSize = settings.Beam,
                MaxLength = settings.MaxLength,
                Llm = new Centurion.Models.Llm.LlmOptions
                {
                    Model = settings.Model,
                    ApiKey = settings.ApiKey,
                    ProviderName = settings.LlmProvider,
                    BaseUrl = settings.LlmBaseUrl
                }
            });
            logger.LogInformation("Using translation strategy: {Strategy}", strategy.StrategyName);
            var translationOp = ActivatorUtilities.CreateInstance<TranslationOperator>(
                serviceProvider, strategy, options);
            var dag = BuildTranslateDag(translationOp, qualityReportOp);

            // --dry-run previews the DAG, models, and costs without running operators.
            if (settings.DryRun)
                return await DryRunHelper.PreviewAsync(dag, workflowContext.Config, serviceProvider, settings.Json, ct);

            var stepResults = await pipelineExecutor.ExecuteAsync(dag, workflowContext, ct);
            var skipped = stepResults.Where(r => r.Status == PipelineStepStatus.Skipped).Select(r => r.Name).ToList();
            if (skipped.Count > 0)
                logger.LogInformation("Skipped {Count} conditional step(s): {Names}", skipped.Count, string.Join(", ", skipped));

            // 5) Save the translated intermediate file; write translations to TranslatedText and preserve timings.
            var outDoc = CenturionDocumentBuilder.Create(workflowContext, "translate", outputPath);
            await store.SaveAsync(outDoc, outputPath, ct);

            var translatedCount = sentences.Count(s => !string.IsNullOrWhiteSpace(s.TranslatedText));
            ConsoleServices.Output.WriteSuccess(ConsoleServices.T("Translation completed"));
            ConsoleServices.Output.WriteInfo(ConsoleServices.T("Translated {0}/{1} sentences -> {2}", translatedCount, sentences.Count, settings.TargetLanguage));
            ConsoleServices.Output.WriteInfo(ConsoleServices.T("Build subtitles with: {0}", "Centurion build <file>.centurion.json"));
            if (settings.Json)
            {
                JsonOutput.Write(new
                {
                    command = "translate",
                    status = "ok",
                    input = inputPath,
                    output = outputPath,
                    translated = translatedCount,
                    total = sentences.Count,
                    steps = workflowContext.State.StepTimings?.Select(kv => new { name = kv.Key, elapsedSeconds = kv.Value.TotalSeconds })
                });
            }
            return ExitCodes.Success;
        }
        catch (Exception ex)
        {
            CliErrorPrinter.Print(logger, ex, "Translation pipeline execution failed.");
            return ExitCodes.Failure;
        }
    }

    /// <summary>
    /// Assembles the translate DAG (single source of truth shared with the pipeline graph command).
    /// </summary>
    internal static PipelineDag BuildTranslateDag(
        TranslationOperator translationOp,
        QualityReportOperator qualityReportOp)
    {
        var builder = PipelineDag.CreateBuilder();
        builder
            .Add("Translation", translationOp, description: "Parallel batched LLM translation (glossary/script constraints)")
            .Add("Quality Report", qualityReportOp, dependsOn: ["Translation"], description: "Translation quality report");
        return builder.Build();
    }

    /// <summary>Reads the target-language script: each non-empty line is one target-language translation.</summary>
    /// <param name="path">Script file path; returns an empty list when null or missing.</param>
    /// <param name="logger">Logs read warnings.</param>
    /// <returns>The script lines.</returns>
    private static List<string> LoadScriptLines(string? path, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(path))
            return [];

        if (!File.Exists(path))
        {
            logger.LogWarning("Target script file not found: {Path}", path);
            return [];
        }

        return File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();
    }
}
