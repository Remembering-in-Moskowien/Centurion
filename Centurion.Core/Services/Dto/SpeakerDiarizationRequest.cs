namespace Centurion.Core.Services.Dto;

public class SpeakerDiarizationRequest
{
    public required string AudioFilePath { get; set; }
    public string? ModelName { get; set; } = "pyannote/speaker-diarization-3.1";
    public int NumSpeakers { get; set; } = 0; // 0 表示自动检测
    public float Threshold { get; set; } = 0.5f;
}