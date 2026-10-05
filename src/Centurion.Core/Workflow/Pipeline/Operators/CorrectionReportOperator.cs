using System.Diagnostics;
using Centurion.Abstractions.Pipeline;
using Centurion.Core.Capabilities.Infrastructure;using Centurion.Models;
using Centurion.Models.Workflow;
using Centurion.Core.Processing.Text;using Microsoft.Extensions.Logging;
using Centurion.Models.Console;
namespace Centurion.Core.Workflow.Pipeline.Operators;

/// <summary>
/// Correction-report operator: aggregates metrics such as total sentence count, text coverage,
/// and timeline drift from the workflow state and correction metadata, prints them to the
/// console, and writes them into the Report in the <see cref="SubtitleWorkflowContext"/> state.
/// </summary>
public sealed class CorrectionReportOperator : PipelineOperatorBase<CorrectionReportOperator>
{
    /// <summary>Creates a correction-report operator instance.</summary>
    /// <param name="logger">Logger used to record report-output logs.</param>
    public CorrectionReportOperator(ILogger<CorrectionReportOperator> logger) : base(logger)
    {
    }

    /// <summary>Display name of the operator in the pipeline.</summary>
    public override string Name => "Correction Report";

    /// <summary>
    /// Aggregates and outputs the correction report: counts total sentences, text coverage,
    /// timeline drift, and elapsed time; writes into Report and prints it.
    /// </summary>
    /// <param name="context">Subtitle workflow context, providing sentences, metadata, and the report object.</param>
    /// <param name="cancellationToken">Cancellation token used to cancel report generation.</param>
    public override Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        cancellationToken.ThrowIfCancellationRequested();
        var sentences = context.State.CurrentSentences;
        var metadata = context.State.CorrectionMetadata;
        var report = context.State.Report;
        report.TotalSentences = context.State.SubtitleSentences.Count > 0
            ? context.State.SubtitleSentences.Count
            : sentences.Count;
        report.TextCoverage = report.TotalSentences == 0 ? 0 : (double)sentences.Count / report.TotalSentences;
        report.TimelineShifted = metadata.Values.Count(values =>
            values.TryGetValue(CorrectKeys.Action, out var action) && Equals(action, "retimed"));
        var drifts = metadata.Values
            .Where(values => values.TryGetValue(CorrectKeys.DriftMs, out _))
            .Select(values => Math.Abs(Convert.ToDouble(values[CorrectKeys.DriftMs])))
            .ToList();
        report.AverageDriftMs = drifts.Count == 0 ? 0 : drifts.Average();
        stopwatch.Stop();
        report.Elapsed = context.State.StepTimings.Count > 0
            ? context.State.StepTimings.Values.Aggregate(TimeSpan.Zero, (total, elapsed) => total + elapsed)
            : stopwatch.Elapsed;

        ConsoleServices.Output.WriteSuccess(ConsoleServices.T("Correction complete: {0} sentences, text coverage {1:P1}, average drift {2:F0}ms", report.TotalSentences, report.TextCoverage, report.AverageDriftMs));
        Logger.LogInformation("Correction report: Total={Total}, TextCorrected={TextCorrected}, TimelineShifted={TimelineShifted}, Unmatched={Unmatched}, AverageDriftMs={Drift:F0}, TextCoverage={Coverage:P1}, Elapsed={Elapsed}", report.TotalSentences, report.TextCorrected, report.TimelineShifted, report.Unmatched, report.AverageDriftMs, report.TextCoverage, report.Elapsed);
        OnProgress(100, "Correction complete");
        return Task.CompletedTask;
    }
}
