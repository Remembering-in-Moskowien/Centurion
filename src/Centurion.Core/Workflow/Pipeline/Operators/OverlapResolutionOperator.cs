using Centurion.Abstractions.Pipeline;
using Centurion.Models.Workflow;
using Microsoft.Extensions.Logging;
using Centurion.Core.Utils.Parsing;
namespace Centurion.Core.Workflow.Pipeline.Operators;

/// <summary>
/// 时间轴重叠消解算子：对当前句子列表按时间排序并消除相邻句的时间重叠，
/// 确保最终输出的字幕时间轴严格单调（后块开始时间不小于前块结束时间）。
/// </summary>
public sealed class OverlapResolutionOperator : PipelineOperatorBase<OverlapResolutionOperator>
{
    /// <summary>创建时间轴重叠消解算子实例。</summary>
    /// <param name="logger">记录消解过程日志的记录器。</param>
    public OverlapResolutionOperator(ILogger<OverlapResolutionOperator> logger) : base(logger)
    {
    }

    /// <summary>算子在管道中的显示名称。</summary>
    public override string Name => "Resolve Overlaps";

    /// <summary>
    /// 对 <see cref="SubtitleWorkflowContext"/> 当前句子执行时间轴重叠消解，
    /// 就地修正并重排句子列表；无重叠时保持原状。
    /// </summary>
    /// <param name="context">字幕工作流上下文，提供当前句子列表。</param>
    /// <param name="cancellationToken">用于取消消解过程的取消标记。</param>
    public override Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var sentences = context.State.CurrentSentences;
        if (sentences.Count < 2)
        {
            LogInfo("Skipping overlap resolution: fewer than 2 sentences.");
            return Task.CompletedTask;
        }

        var changed = TimelineOverlapResolver.Resolve(sentences);
        if (changed > 0)
        {
            LogInfo($"Resolved overlaps for {changed} sentences.");
            context.State.Warnings.Add($"Overlap resolution adjusted {changed} sentence timing(s).");
        }
        else
        {
            LogInfo("No overlapping subtitles detected.");
        }

        return Task.CompletedTask;
    }
}
