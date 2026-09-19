namespace Centurion.Abstractions.Pipeline;

/// <summary>
/// 算子进度事件参数
/// </summary>
public class OperatorProgressEventArgs : EventArgs
{
    /// <summary>
    /// 报告进度的算子名称。
    /// </summary>
    public string OperatorName { get; init; } = string.Empty;

    /// <summary>
    /// 当前完成百分比（0–100）。
    /// </summary>
    public int Percentage { get; init; } // 0-100

    /// <summary>
    /// 可选的状态描述文本，用于补充说明当前进展。
    /// </summary>
    public string? StatusMessage { get; init; }

    /// <summary>
    /// 进度事件产生的时间戳。
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.Now;
}
