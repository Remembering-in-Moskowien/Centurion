namespace Centurion.Abstractions;

/// <summary>
/// Generic interface for download operators.
/// </summary>
/// <typeparam name="TRequest">Request payload type.</typeparam>
/// <typeparam name="TResponse">Result type returned by the operator.</typeparam>
public interface IOperator<TRequest, TResponse> : IDisposable
{
    /// <summary>
    /// Checks whether the underlying executable is available.
    /// </summary>
    Task CheckHealthAsync();

    /// <summary>
    /// Processes an operator request asynchronously with cancellation support.
    /// </summary>
    /// <param name="request">The operator request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<TResponse> ProcessAsync(
        OperatorsRequest<TRequest> request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Operator request container.
/// </summary>
/// <typeparam name="TPayload">Business payload type.</typeparam>
public class OperatorsRequest<TPayload>
{
    /// <summary>
    /// Business payload containing the input data required by the operator.
    /// </summary>
    public required TPayload Payload { get; init; }
}