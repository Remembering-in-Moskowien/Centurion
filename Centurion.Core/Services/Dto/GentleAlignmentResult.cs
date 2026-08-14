namespace Centurion.Core.Services.Dto;

public class GentleAlignmentResult
{
    public string Transcript { get; set; } = string.Empty;
    public List<GentleWord> Words { get; set; } = [];
}

public class GentleWord
{
    public string Word { get; set; } = string.Empty;
    public double Start { get; set; } // 秒
    public double End { get; set; } // 秒
    public string Case { get; set; } = string.Empty;
}