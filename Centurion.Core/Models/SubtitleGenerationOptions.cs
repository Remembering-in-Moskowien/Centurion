namespace Centurion.Core.Models;

public class SubtitleGenerationOptions
{
    public string ModelName { get; init; } = "base";
    public string Language { get; init; } = "en";
    public string? InitialPrompt { get; init; }
    public int MaxLength { get; init; } = 80;
    public int TargetLength { get; init; } = 50;
    public int SpreadRange { get; init; } = 10;
    public int NumSpeakers { get; set; } = 0;
    public bool Karaoke { get; set; } = false;
    public bool UseGentle { get; set; }
    public string GentleUrl { get; init; } = "http://localhost:8765";
    public bool UseMfa { get; set; }
}