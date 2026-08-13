namespace Centurion.Core.Operators.Results;
public class WordMergeResult
{
    public required List<SentenceResult> Sentences { get; set; }
}

public class SentenceResult
{
    public required string Text { get; set; }
    public double Start { get; set; }
    public double End   { get; set; }
}