using Centurion.Models;
using Centurion.Models.Workflow;

namespace Centurion.Abstractions.Pipeline;

/// <summary>
/// Base interface for pipeline operators.
/// Follows a data-driven design: business data flows through SubtitleWorkflowContext,
/// and operators transform that context.
/// </summary>
public interface IPipelineOperator
{
    /// <summary>
    /// Operator name used in logs and progress displays.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Executes the pipeline transformation.
    /// The operator reads configuration from context.Config and input from context.State,
    /// then writes its results back to context.State.
    /// </summary>
    /// <param name="context">The complete workflow context, passed by reference.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken = default);
}
