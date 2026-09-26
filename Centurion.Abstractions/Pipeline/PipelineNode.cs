using Centurion.Models.Workflow;

namespace Centurion.Abstractions.Pipeline;

/// <summary>
/// DAG 节点：封装一个算子及其依赖、条件、重试与超时策略。
/// 依赖通过 <see cref="DependsOn"/> 声明（节点名），由 DAG 执行器按拓扑调度；
/// 无依赖（或依赖已全部完成）的节点可并行执行。
/// </summary>
public sealed class PipelineNode
{
    /// <summary>节点唯一名称（日志、依赖引用、计时键）。</summary>
    public required string Name { get; init; }

    /// <summary>实际执行管线变换的算子。</summary>
    public required IPipelineOperator Operator { get; init; }

    /// <summary>依赖的节点名列表；为空表示无依赖（可与其它就绪节点并行）。</summary>
    public IReadOnlyList<string> DependsOn { get; init; } = [];

    /// <summary>条件谓词；返回 false 时节点被跳过（不执行、不计失败）。为 null 时总是执行。</summary>
    public Func<SubtitleWorkflowContext, bool>? When { get; init; }

    /// <summary>失败重试次数（不含首次执行）。默认 0 不重试。</summary>
    public int MaxRetries { get; init; }

    /// <summary>单次执行超时；超时视为该次尝试失败。为 null 表示不设超时。</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>重试耗尽后是否降级：true 时记录 Degraded 并继续执行其它节点，false 时终止任务。</summary>
    public bool DegradeOnFailure { get; init; }

    /// <summary>节点说明（pipeline graph 可视化用）。</summary>
    public string? Description { get; init; }
}
