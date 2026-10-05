namespace Centurion.Abstractions.Pipeline;

/// <summary>
/// Optional interface for pipeline operators that support health checks.
/// Use it when an operator must validate external dependencies, such as model files or FFmpeg.
/// </summary>
public interface IHealthCheckableOperator : IPipelineOperator
{
    /// <summary>
    /// Checks whether the operator's runtime environment is ready.
    /// </summary>
    Task CheckHealthAsync(CancellationToken cancellationToken = default);
}
