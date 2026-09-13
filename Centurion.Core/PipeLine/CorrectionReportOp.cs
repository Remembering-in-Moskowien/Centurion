using System.Diagnostics;
using Centurion.Core.Abstractions;
using Centurion.Core.Models;
using Centurion.Core.Text;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.PipeLine;

public sealed class CorrectionReportOp : PipelineOperatorBase<CorrectionReportOp>
{
    public CorrectionReportOp(ILogger<CorrectionReportOp> logger) : base(logger)
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
        report.Removed = sentences.Count(sentence => sentence.SkipRender);
        var nonSpont = sentences.Count(sentence =>
            metadata.TryGetValue(sentence, out var values) &&
            (!values.TryGetValue(CorrectKeys.Origin, out var origin) || !Equals(origin, "spont")) &&
            (!values.TryGetValue(CorrectKeys.Action, out var action) || !Equals(action, "removed")));
        report.TextCoverage = report.TotalSentences == 0 ? 0 : (double)nonSpont / report.TotalSentences;
        var drifts = metadata.Values
            .Where(values => values.TryGetValue(CorrectKeys.DriftMs, out _))
            .Select(values => Math.Abs(Convert.ToDouble(values[CorrectKeys.DriftMs])))
            .ToList();
        report.AverageDriftMs = drifts.Count == 0 ? 0 : drifts.Average();
        stopwatch.Stop();
        report.Elapsed = stopwatch.Elapsed;

        ConsoleServices.Output.WriteMarkupLine($"[green]Correction complete:[/] {report.TotalSentences} sentences, text coverage {report.TextCoverage:P1}, average drift {report.AverageDriftMs:F0}ms");
        Logger.LogInformation("Correction report: Total={Total}, TextCorrected={TextCorrected}, TimelineShifted={TimelineShifted}, Unmatched={Unmatched}, Removed={Removed}, TextCoverage={Coverage:P1}, AverageDriftMs={Drift:F0}", report.TotalSentences, report.TextCorrected, report.TimelineShifted, report.Unmatched, report.Removed, report.TextCoverage, report.AverageDriftMs);
        OnProgress(100, "Correction complete");
        return Task.CompletedTask;
    }
}