namespace Centurion.Core.Operators.Payload;

/// <summary>FFmpeg转换请求载荷，替代原静态方法入参</summary>
public class FFmpegConvertPayload
{
    /// <summary>输入音频路径</summary>
    public string InputFilePath { get; set; } = string.Empty;

    /// <summary>必填，输出文件路径</summary>
    public string? OutputFilePath { get; set; }
}