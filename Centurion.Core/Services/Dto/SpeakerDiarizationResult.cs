namespace Centurion.Core.Services.Dto;

public class SpeakerSegment
{
    public double Start { get; set; } // 秒
    public double End { get; set; }
    public string Speaker { get; set; } = string.Empty;
}

public class SpeakerDiarizationResult
{
    public List<SpeakerSegment> Segments { get; init; } = [];
}