using Centurion.Core.Models;

namespace Centurion.Core.Response;

/// <summary>
/// 字幕生成算子返回结果
/// </summary>
public class SubtitleGenerationResponse
{
    /// <summary>生成的 ASS 字幕文档对象</summary>
    public required AssSub Document { get; init; }
}