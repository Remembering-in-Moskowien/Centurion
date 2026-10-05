namespace Centurion.Abstractions.Pipeline;

/// <summary>
/// Optional interface for pipeline operators that report progress.
/// Use it for long-running tasks that need to update the console or UI.
/// </summary>
public interface IProgressReportableOperator : IPipelineOperator
{
    /// <summary>
    /// Progress event that subscribers, such as CLI commands, can use to render a progress bar.
    /// </summary>
    event EventHandler<OperatorProgressEventArgs>? Progress;
}
