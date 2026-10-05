namespace Centurion.Abstractions.Pipeline;

/// <summary>
/// Event arguments for operator progress updates.
/// </summary>
public class OperatorProgressEventArgs : EventArgs
{
    /// <summary>
    /// Name of the operator reporting progress.
    /// </summary>
    public string OperatorName { get; init; } = string.Empty;

    /// <summary>
    /// Current completion percentage (0–100).
    /// </summary>
    public int Percentage { get; init; } // 0-100

    /// <summary>
    /// Optional status text describing the current progress.
    /// </summary>
    public string? StatusMessage { get; init; }

    /// <summary>
    /// Timestamp when the progress event was raised.
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.Now;
}
