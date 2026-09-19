namespace Centurion.Core.Operators.Response;

/// <summary>文件输出模式返回结果</summary>
public class FFmpegConvertResponse
{
    /// <summary>转换后输出文件的路径。</summary>
    public string OutputPath { get; set; } = string.Empty;
}

/// <summary>FFmpeg 音频分割的返回结果。</summary>
public class FFmpegSplitResponse
{
    /// <summary>各分段输出文件的路径列表。</summary>
    public List<string> OutputFiles { get; set; } = [];
}