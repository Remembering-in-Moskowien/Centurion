using Centurion.Abstractions.Pipeline;
using Centurion.Models.Providers;
using Centurion.Models.Workflow;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using System.Diagnostics;
using Centurion.Core.Utils.Reporting;
namespace Centurion.Core.Workflow.Pipeline.Operators;

/// <summary>
/// Quality report operator: after all processing completes, collects metrics from the workflow context and writes .quality.json.
/// Every pipeline path (spawn/from-script/correct/convert/translate/dub) appends this operator at the end of the pipeline.
/// </summary>
public sealed class QualityReportOperator(ILogger<QualityReportOperator> logger)
    : PipelineOperatorBase<QualityReportOperator>(logger)
{
    internal static readonly JsonSerializerSettings SerializerSettings = new()
    {
        Formatting = Formatting.Indented,
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        NullValueHandling = NullValueHandling.Ignore,
        Converters = [new StringEnumConverter()]
    };

    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    /// <summary>Operator name.</summary>
    public override string Name => "Quality Report";

    /// <summary>
    /// Builds the quality report and writes .quality.json next to the output subtitle.
    /// </summary>
    /// <param name="context">Workflow context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public override Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken)
    {
        var outputPath = context.Config.OutputFilePath ?? Path.ChangeExtension(context.Config.InputFilePath, ".ass");
        var report = QualityReportBuilder.Build(context, outputPath, _stopwatch.Elapsed.TotalSeconds);

        var reportPath = Path.ChangeExtension(outputPath, ".quality.json");
        File.WriteAllText(reportPath, JsonConvert.SerializeObject(report, SerializerSettings));

        var htmlPath = Path.ChangeExtension(outputPath, ".quality.html");
        File.WriteAllText(htmlPath, QualityHtmlReport.Render(report));

        context.State.Extensions["QualityReportPath"] = reportPath;
        context.State.Extensions["QualityReportHtmlPath"] = htmlPath;

        LogProviderUsage(context);

        LogInfo($"Quality report written to '{reportPath}' (sentences: {report.Counts.SentenceCount}, speakers: {report.Counts.SpeakerCount}); HTML: '{htmlPath}'.");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Logs a provider usage summary (tokens / audio minutes / cache hits / estimated cost).
    /// Data comes from <see cref="WorkflowState.ProviderUsages"/> (in-process aggregation).
    /// </summary>
    private void LogProviderUsage(SubtitleWorkflowContext context)
    {
        var usages = context.State.ProviderUsages;
        if (usages.Count == 0)
            return;

        var tokensIn = usages.Sum(u => u.TokensIn);
        var tokensOut = usages.Sum(u => u.TokensOut);
        var audioMinutes = usages.Sum(u => u.AudioSeconds) / 60.0;
        var cacheHits = usages.Sum(u => u.CacheHits);
        var cost = usages.Sum(u => u.EstimatedCostUsd);
        var via = string.Join(", ", usages.Select(u => u.ProviderName).Distinct());
        LogInfo($"[provider] usage: tokens {tokensIn}+{tokensOut}, audio {audioMinutes:F2}m, cache hits {cacheHits}, est. cost ${cost:F4} (via {via})");
    }
}
