namespace Centurion.Models;

/// <summary>Word-level transcription unit containing text, timing, speaker, part-of-speech tag, and alignment status.</summary>
public class Word
{
    /// <summary>Word text.</summary>
    public required string Text { get; set; }
    /// <summary>Word start time in milliseconds.</summary>
    public double Start { get; set; }
    /// <summary>Word end time in milliseconds.</summary>
    public double End { get; set; }
    /// <summary>Speaker identifier for this word; may be empty when diarization was not performed.</summary>
    public required string Speaker { get; set; }
    /// <summary>Part-of-speech tag, or null when not annotated.</summary>
    public string? PosTag { get; set; }
    /// <summary>Word-level ASR confidence (0–1), or null when not provided by the model.</summary>
    public double? Confidence { get; set; }
    /// <summary>Alignment status between this word and the script; defaults to <see cref="MappingStatus.Matched"/>.</summary>
    public MappingStatus Status { get; set; } = MappingStatus.Matched;
}
