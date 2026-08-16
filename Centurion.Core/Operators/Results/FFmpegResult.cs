namespace Centurion.Core.Operators.Results;

/// <summary>文件输出模式返回结果</summary>
public class FFmpegConvertResult
{
    public string OutputPath { get; set; } = string.Empty;
}

public class FFmpegSplitResult
{
    public List<string> OutputFiles { get; set; } = new();
}