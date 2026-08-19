using Centurion.Core.Models;

namespace Centurion.Core.Response;

/// <summary>
/// SRT 转换算子返回结果
/// </summary>
public class SubtitleConvertResponse
{
    /// <summary>生成的 ASS 字幕文档对象</summary>
    public required AssSub Document { get; init; }
}