namespace Centurion.Core.Models;

public class Sentence
{
    public string Text { get; set; } = string.Empty;
    public double Start { get; set; }
    public double End { get; set; }
    public List<Word> Words { get; set; } = [];
}

public class Word
{
    public required string Text { get; set; }
    public double Start { get; set; }
    public double End { get; set; }
    public required string Speaker { get; set; }
}