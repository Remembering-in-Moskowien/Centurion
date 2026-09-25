using Centurion.Models.Console;
using System.Diagnostics;
using Centurion.Abstractions.Pipeline;
using Centurion.Core.Infrastructure;
using Centurion.Models;
using Centurion.Models.Workflow;
using Centurion.Core.Text;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Pipeline.Operators;

/// <summary>
/// 校正报告算子：基于工作流状态与校正元数据汇总句子总数、文本覆盖率、时间漂移等指标，
/// 输出到控制台并写入 <see cref="SubtitleWorkflowContext"/> 状态中的 Report。
/// </summary>
public sealed class CorrectionReportOperator : PipelineOperatorBase<CorrectionReportOperator>
{
    /// <summary>创建校正报告算子实例。</summary>
    /// <param name="logger">记录报告输出日志的记录器。</param>
    public CorrectionReportOperator(ILogger<CorrectionReportOperator> logger) : base(logger)
    {
    }

    /// <summary>算子在管道中的显示名称。</summary>
    public override string Name => "Correction Report";

    /// <summary>
    /// 汇总并输出校正报告：统计句子总数、文本覆盖率、时间漂移与耗时，写入 Report 并打印。
    /// </summary>
    /// <param name="context">字幕工作流上下文，提供句子、元数据与报告对象。</param>
    /// <param name="cancellationToken">用于取消报告生成的取消标记。</param>
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

        ConsoleServices.Output.WriteSuccess(ConsoleServices.T("Correction complete: {0} sentences, text coverage {1:P1}, average drift {2:F0}ms", report.TotalSentences, report.TextCoverage, report.AverageDriftMs));
        Logger.LogInformation("Correction report: Total={Total}, TextCorrected={TextCorrected}, TimelineShifted={TimelineShifted}, Unmatched={Unmatched}, AverageDriftMs={Drift:F0}, TextCoverage={Coverage:P1}, Elapsed={Elapsed}", report.TotalSentences, report.TextCorrected, report.TimelineShifted, report.Unmatched, report.AverageDriftMs, report.TextCoverage, report.Elapsed);
        OnProgress(100, "Correction complete");
        return Task.CompletedTask;
    }
}
