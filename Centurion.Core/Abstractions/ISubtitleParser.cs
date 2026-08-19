using Centurion.Core.Models;

namespace Centurion.Core.Abstractions;

/// <summary>
/// 字幕解析器接口，每种格式实现一个
/// </summary>
public interface ISubtitleParser
{
    /// <summary>支持的扩展名（如 ".srt"）</summary>
    string SupportedExtension { get; }

    /// <summary>解析文件为字幕条目列表</summary>
    Task<List<SubtitleEntry>> ParseAsync(string filePath, CancellationToken ct = default);
}