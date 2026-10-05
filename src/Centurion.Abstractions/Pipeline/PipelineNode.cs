using Centurion.Models.Workflow;

namespace Centurion.Abstractions.Pipeline;

/// <summary>
/// DAG node that encapsulates an operator, its dependencies, conditions, retries, and timeout policy.
/// Dependencies are declared by name in <see cref="DependsOn"/> and scheduled topologically by the DAG executor.
/// Nodes with no dependencies, or with all dependencies completed, can run in parallel.
/// </summary>
public sealed class PipelineNode
{
    /// <summary>Unique node name used in logs, dependency references, and timing keys.</summary>
    public required string Name { get; init; }

    /// <summary>Operator that performs the pipeline transformation.</summary>
    public required IPipelineOperator Operator { get; init; }

    /// <summary>Names of dependency nodes; empty means this node can run with other ready nodes.</summary>
    public IReadOnlyList<string> DependsOn { get; init; } = [];

    /// <summary>Condition predicate; false skips the node without execution or failure. Null always runs the node.</summary>
    public Func<SubtitleWorkflowContext, bool>? When { get; init; }

    /// <summary>Number of retries after the initial attempt. Defaults to 0.</summary>
    public int MaxRetries { get; init; }

    /// <summary>Timeout for one execution attempt; a timeout counts as a failure. Null means no timeout.</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>Whether to degrade after retries are exhausted; true records Degraded and continues, while false terminates the task.</summary>
    public bool DegradeOnFailure { get; init; }

    /// <summary>Node description used by the pipeline graph visualization.</summary>
    public string? Description { get; init; }
}
