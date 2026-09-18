namespace Centurion.Abstractions.Pipeline;

/// <summary>
/// 算子进度事件参数
/// </summary>
public class OperatorProgressEventArgs : EventArgs
{
    public string OperatorName { get; init; } = string.Empty;
    public int Percentage { get; init; } // 0-100
    public string? StatusMessage { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.Now;
}
