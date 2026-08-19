namespace Centurion.Core.Request;

/// <summary>
/// 字幕转换算子请求载荷
/// </summary>
public class SubtitleConvertRequest
{
    /// <summary>字幕文件路径</summary>
    public required string FilePath { get; init; }

    /// <summary>可选：显式指定格式（文件扩展名由系统自动识别，若指定则优先）</summary>
    public string? Format { get; init; }
}