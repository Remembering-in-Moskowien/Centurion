using Centurion.Models.Workflow;

namespace Centurion.Abstractions.Pipeline;

/// <summary>管线节点的执行状态。</summary>
public enum PipelineStepStatus
{
    /// <summary>尚未开始。</summary>
    Pending,
    /// <summary>执行中。</summary>
    Running,
    /// <summary>成功完成。</summary>
    Completed,
    /// <summary>条件不满足被跳过。</summary>
    Skipped,
    /// <summary>首次失败后重试成功。</summary>
    Retried,
    /// <summary>重试耗尽后按降级策略跳过（任务整体继续）。</summary>
    Degraded,
    /// <summary>失败且不可降级（任务终止）。</summary>
    Failed
}

/// <summary>单个管线节点的执行结果（耗时、状态、重试次数等）。</summary>
public sealed class PipelineStepResult
{
    /// <summary>节点名称。</summary>
    public required string Name { get; init; }

    /// <summary>最终状态。</summary>
    public required PipelineStepStatus Status { get; init; }

    /// <summary>累计执行耗时（含重试）。</summary>
    public required TimeSpan Elapsed { get; init; }

    /// <summary>实际执行尝试次数（含首次）。</summary>
    public required int Attempts { get; init; }

    /// <summary>最后一次失败时的异常；成功或跳过时为 null。</summary>
    public Exception? Error { get; init; }

    /// <summary>被跳过（条件不满足）时的说明。</summary>
    public string? SkipReason { get; init; }
}
