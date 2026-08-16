namespace Centurion.Core.Operators.Payload;

/// <summary>FFmpeg转换请求载荷，替代原静态方法入参</summary>
public class FFmpegConvertPayload
{
    /// <summary>输入音频路径</summary>
    public string InputFilePath { get; init; } = string.Empty;

    /// <summary>必填，输出文件路径</summary>
    public string? OutputFilePath { get; init; }
}

public class FFmpegSplitPayload
{
    public string InputFilePath { get; set; } = string.Empty;
    public List<(long StartMs, long EndMs)> Segments { get; set; } = new();
    public string OutputDirectory { get; set; } = string.Empty;
}