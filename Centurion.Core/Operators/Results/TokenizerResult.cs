namespace Centurion.Core.Operators.Results;

/// <summary>
/// 分词结果
/// </summary>
public class TokenizationResult
{
    public required List<int> Ids { get; set; }
    public required List<(int Start, int End)> Offsets { get; set; }
    public required string OriginalText { get; set; }
}