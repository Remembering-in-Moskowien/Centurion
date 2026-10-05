using Centurion.Models.Workflow;

namespace Centurion.Abstractions.Pipeline;

/// <summary>Execution status of a pipeline node.</summary>
public enum PipelineStepStatus
{
    /// <summary>Not started.</summary>
    Pending,
    /// <summary>Running.</summary>
    Running,
    /// <summary>Completed successfully.</summary>
    Completed,
    /// <summary>Skipped because its condition was not met.</summary>
    Skipped,
    /// <summary>Retry succeeded after an initial failure.</summary>
    Retried,
    /// <summary>Retries were exhausted, so the node was skipped under the degradation policy and the task continued.</summary>
    Degraded,
    /// <summary>Failed without degradation; the task terminates.</summary>
    Failed
}

/// <summary>Execution result for one pipeline node, including elapsed time, status, and retry count.</summary>
public sealed class PipelineStepResult
{
    /// <summary>Node name.</summary>
    public required string Name { get; init; }

    /// <summary>Final status.</summary>
    public required PipelineStepStatus Status { get; init; }

    /// <summary>Total execution time, including retries.</summary>
    public required TimeSpan Elapsed { get; init; }

    /// <summary>Number of execution attempts, including the initial attempt.</summary>
    public required int Attempts { get; init; }

    /// <summary>Exception from the last failed attempt, or null on success or skip.</summary>
    public Exception? Error { get; init; }

    /// <summary>Reason the node was skipped because its condition was not met.</summary>
    public string? SkipReason { get; init; }
}
