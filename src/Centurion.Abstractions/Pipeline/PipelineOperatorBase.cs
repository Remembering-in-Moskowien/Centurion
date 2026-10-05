using Centurion.Models;
using Centurion.Models.Workflow;
using Microsoft.Extensions.Logging;
using Centurion.Abstractions.Utils;
using Centurion.Models.Console;
namespace Centurion.Abstractions.Pipeline;

/// <summary>
/// Base class for pipeline operators.
/// Provides a name, logging, progress events, and an optional health-check method.
/// Derived classes only need to implement the ExecuteAsync business logic.
/// </summary>
public abstract class PipelineOperatorBase<TLogger> : IPipelineOperator, IProgressReportableOperator
    where TLogger : class
{
    /// <summary>
    /// Initializes the pipeline operator base class with the specified logger.
    /// </summary>
    /// <param name="logger">Logger used by this operator.</param>
    protected PipelineOperatorBase(ILogger<TLogger> logger)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Logger available to derived classes; log entries are automatically prefixed with the operator name.
    /// </summary>
    protected ILogger<TLogger> Logger { get; }

    // ---------- Core abstraction ----------
    /// <summary>
    /// Operator name used in logs and progress displays.
    /// </summary>
    public abstract string Name { get; }

    /// <summary>
    /// Executes the pipeline transformation by reading input from the workflow context and writing results back.
    /// </summary>
    /// <param name="context">The complete workflow context, passed by reference.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public abstract Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken);

    // ---------- Progress events (subscribed to by the caller) ----------
    /// <summary>
    /// Progress event that callers can subscribe to for rendering progress.
    /// </summary>
    public event EventHandler<OperatorProgressEventArgs>? Progress;

    /// <summary>
    /// Raises the progress event. Derived classes call this method when reporting progress.
    /// </summary>
    protected virtual void OnProgress(int percentage, string? message = null)
    {
        Progress?.Invoke(this, new OperatorProgressEventArgs
        {
            OperatorName = Name,
            Percentage = Math.Clamp(percentage, 0, 100),
            StatusMessage = message
        });
        // Optional console output for debugging.
        // ConsoleServices.Output?.WriteLine($"[{Name}] {percentage}% - {message}");
    }

    /// <summary>
    /// Writes an information-level log entry prefixed with the operator name.
    /// </summary>
    /// <param name="message">Log message.</param>
    protected void LogInfo(string message)
    {
        Logger.LogInformation("[{Operator}] {Message}", Name, message);
    }

    /// <summary>
    /// Writes a warning-level log entry prefixed with the operator name.
    /// Operator failures are internal details: log them as warnings and continue; the outermost layer reports the single failure.
    /// </summary>
    /// <param name="message">Log message.</param>
    protected void LogWarning(string message)
    {
        Logger.LogWarning("[{Operator}] {Message}", Name, message);
    }

    // ---------- Health check (optional override) ----------
    /// <summary>
    /// Checks whether the operator runtime is ready; the default implementation succeeds, and derived classes can override it.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public virtual Task CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
