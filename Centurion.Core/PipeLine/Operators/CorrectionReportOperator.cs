using System.Diagnostics;
using Centurion.Core.Abstractions.Pipeline;
using Centurion.Core.Infrastructure;
using Centurion.Core.Models;
using Centurion.Core.Models.Workflow;
using Centurion.Core.Text;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Pipeline.Operators;

public sealed class CorrectionReportOperator : PipelineOperatorBase<CorrectionReportOperator>
{
    public CorrectionReportOperator(ILogger<CorrectionReportOperator> logger) : base(logger)
    {
    }

    public override string Name => "Correction Report";

    public override Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        cancellationToken.ThrowIfCancellationRequested();
        var sentences = context.State.CurrentSentences;
        var metadata = CorrectionMetadata.Get(context.State);
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
        report.Elapsed = context.State.Extensions.TryGetValue("StepTimings", out var timingsValue) &&
                         timingsValue is Dictionary<string, TimeSpan> timings
            ? timings.Values.Aggregate(TimeSpan.Zero, (total, elapsed) => total + elapsed)
            : stopwatch.Elapsed;

        ConsoleServices.Output.WriteMarkupLine($"[green]Correction complete:[/] {report.TotalSentences} sentences, text coverage {report.TextCoverage:P1}, average drift {report.AverageDriftMs:F0}ms");
        Logger.LogInformation("Correction report: Total={Total}, TextCorrected={TextCorrected}, TimelineShifted={TimelineShifted}, Unmatched={Unmatched}, AverageDriftMs={Drift:F0}, TextCoverage={Coverage:P1}, Elapsed={Elapsed}", report.TotalSentences, report.TextCorrected, report.TimelineShifted, report.Unmatched, report.AverageDriftMs, report.TextCoverage, report.Elapsed);
        OnProgress(100, "Correction complete");
        return Task.CompletedTask;
    }
}
