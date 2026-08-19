namespace Centurion.Core.Request;

/// <summary>FFmpeg转换请求载荷，替代原静态方法入参</summary>
public class FFmpegConvertRequest
{
    /// <summary>输入音频路径</summary>
    public string InputFilePath { get; init; } = string.Empty;

    /// <summary>必填，输出文件路径</summary>
    public string? OutputFilePath { get; init; }
}

// 在 FFmpegSplitRequest 类中添加 OutputFileNames 属性
public class FFmpegSplitRequest
{
    public required string InputFilePath { get; init; }
    public required List<(long StartMs, long EndMs)> Segments { get; init; }
    public string? OutputDirectory { get; set; }   // 可选，当 OutputFileNames 为空时使用
    public List<string>? OutputFileNames { get; set; } // 可选，若提供则必须与 Segments 长度一致
}