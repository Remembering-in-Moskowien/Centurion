namespace Centurion.Models.Workflow;

/// <summary>
/// The full context of the subtitle generation workflow, the only object passed through the Pipeline.
/// Designed as a pure data container and serializable to support checkpoints and resume.
/// </summary>
public class SubtitleWorkflowContext(WorkflowConfig config)
{
    /// <summary>Workflow configuration; originates from the CLI subcommand and may be updated mid-chain, such as the output path.</summary>
    public WorkflowConfig Config { get; set; } = config;

    /// <summary>Mutable workflow state, filled in step by step by each Operator.</summary>
    public WorkflowState State { get; set; } = new();
}
