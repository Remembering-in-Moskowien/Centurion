namespace Centurion.Core.Services.Dto;

public class SatSplitResult
{
    public string Text { get; set; } = string.Empty;
    public double Start { get; set; }
    public double End { get; set; }
    public List<WordTiming> Words { get; set; } = [];
}