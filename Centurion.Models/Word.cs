namespace Centurion.Models;

public class Word
{
    public required string Text { get; set; }
    public double Start { get; set; }
    public double End { get; set; }
    public required string Speaker { get; set; }
    public string? PosTag { get; set; }
    public MappingStatus Status { get; set; } = MappingStatus.Matched;
}
