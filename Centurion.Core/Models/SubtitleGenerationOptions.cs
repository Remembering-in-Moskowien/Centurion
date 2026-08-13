namespace Centurion.Core.Models;

public class SubtitleGenerationOptions
{
    public string ModelName { get; set; } = "base";
    public string Language { get; set; } = "en";
    public string? InitialPrompt { get; set; }
    public int MaxLength { get; set; } = 80;
    public int TargetLength { get; set; } = 50;
    public int SpreadRange { get; set; } = 10;
}