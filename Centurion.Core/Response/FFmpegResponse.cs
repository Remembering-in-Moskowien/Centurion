namespace Centurion.Core.Response;

/// <summary>文件输出模式返回结果</summary>
public class FFmpegConvertResponse
{
    public string OutputPath { get; set; } = string.Empty;
}

public class FFmpegSplitResponse
{
    public List<string> OutputFiles { get; set; } = [];
}