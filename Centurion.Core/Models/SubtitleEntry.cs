namespace Centurion.Core.Models;

/// <summary>
/// 字幕条目中间表示（用于不同格式的统一转换）
/// </summary>
public class SubtitleEntry
{
    public long StartMs { get; set; }
    public long EndMs { get; set; }
    public string Text { get; set; } = string.Empty;   // 可能包含多行，使用 \N 占位符
}