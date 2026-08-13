using Centurion.Core.Operators.Results;
namespace Centurion.Core.Operators.Payload;

/// <summary>句子块合并+ONNX语义分句算子入参，前缀WordMerge区分转录原生实体</summary>
public class WordMergePayload
{
    public required List<WordTiming> Words { get; set; }
    public string? Language { get; set; } // 可选，供分句器参考
}

public class WordTiming
{
    public required string Text { get; set; }
    public double Start { get; set; }
    public double End   { get; set; }
}

