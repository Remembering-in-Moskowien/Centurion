namespace Centurion.Models;

public class Sentence
{
    public string Text { get; set; } = string.Empty;
    public string? CleanedText { get; set; }
    public double Start { get; set; }
    public double End { get; set; }
    public bool SkipRender { get; set; }
    public List<Word> Words { get; set; } = [];
}
