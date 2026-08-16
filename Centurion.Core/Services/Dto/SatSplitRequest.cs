namespace Centurion.Core.Services.Dto;

/// <summary>句子块合并+ONNX语义分句算子入参，前缀WordMerge区分转录原生实体</summary>
public class SatSplitRequest
{
    public required List<WordTiming> Words { get; set; }
    public string? Language { get; set; } // 可选，供分句器参考
    public int? MaxLength { get; set; }
    public int? TargetLength { get; set; }
    public int? SpreadRange { get; set; }
}

public class WordTiming
{
    public required string Text { get; init; }
    public double Start { get; init; }
    public double End { get; init; }
    public required string Speaker { get; set; }
}