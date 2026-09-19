namespace Centurion.Core.Operators.Request;

/// <summary>FFmpeg转换请求载荷，替代原静态方法入参</summary>
public class FFmpegConvertRequest
{
    /// <summary>输入音频路径</summary>
    public string InputFilePath { get; init; } = string.Empty;

    /// <summary>必填，输出文件路径</summary>
    public string? OutputFilePath { get; init; }
}

/// <summary>FFmpeg 音频分割请求载荷，描述待切割的输入文件与分段信息。</summary>
public class FFmpegSplitRequest
{
    /// <summary>待分割的输入音频文件路径。</summary>
    public required string InputFilePath { get; init; }
    /// <summary>各分段的起止时间（毫秒）列表。</summary>
    public required List<(long StartMs, long EndMs)> Segments { get; init; }
    /// <summary>可选的输出目录；未提供 OutputFileNames 时用于生成输出文件。</summary>
    public string? OutputDirectory { get; set; } // 可选，当 OutputFileNames 为空时使用
    /// <summary>可选的输出文件名列表；若提供则必须与 Segments 长度一致。</summary>
    public List<string>? OutputFileNames { get; set; } // 可选，若提供则必须与 Segments 长度一致
}