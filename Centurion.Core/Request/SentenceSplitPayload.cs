using Centurion.Core.Models;

namespace Centurion.Core.Request;

/// <summary>句子块合并+ONNX语义分句算子入参，前缀WordMerge区分转录原生实体</summary>
public class SentenceSplitRequest
{
    public required Sentence Sentence { get; set; }
    public string? Language { get; set; } // 可选，供分句器参考
    public int? MaxLength { get; set; }
    public int? TargetLength { get; set; }
    public int? SpreadRange { get; set; }
}