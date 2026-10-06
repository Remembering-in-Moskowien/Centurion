namespace Centurion.Models.Workflow;

/// <summary>
/// A single synthesis segment in the dub (media localization) pipeline: target text, speaker reference, timeline constraints, and synthesized output.
/// Produced by the TTS synthesis operator and consumed by the time-alignment and mixing operators.
/// </summary>
public class DubSegment
{
    /// <summary>Target language text to be dubbed, with surrounding whitespace removed.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Speaker ID; null when no diarization was performed.</summary>
    public string? SpeakerId { get; set; }

    /// <summary>Path to the speaker reference audio; provided when diarization ran and the media is available.</summary>
    public string? ReferenceAudioPath { get; set; }

    /// <summary>Target start time in milliseconds, taken from the subtitle timeline.</summary>
    public double TargetStartMs { get; set; }

    /// <summary>Target end time in milliseconds.</summary>
    public double TargetEndMs { get; set; }

    /// <summary>Path of the synthesized wav output after TTS.</summary>
    public string? SynthesizedWavPath { get; set; }

    /// <summary>Raw TTS synthesis duration in seconds, probed by ffprobe.</summary>
    public double SynthesizedDurationSec { get; set; }

    /// <summary>Duration after time alignment in seconds, after atempo adjustment.</summary>
    public double AlignedDurationSec { get; set; }

    /// <summary>Final mix position offset in milliseconds; used to overlap adjacent segments when they overlap.</summary>
    public double MixOffsetMs { get; set; }

    /// <summary>Final applied tempo ratio via atempo; 1.0 when no adjustment was needed.</summary>
    public double AlignmentTempo { get; set; } = 1.0;

    /// <summary>Emotion hint detected from the subtitle text: excited/sad/angry/neutral; currently recorded only and does not affect synthesis.</summary>
    public string? EmotionHint { get; set; }

    /// <summary>Whether synthesis was skipped; set when TTS fails to avoid null references during the mixing stage.</summary>
    public bool Skipped { get; set; }

    /// <summary>Note on skipping or processing, such as the TTS failure reason.</summary>
    public string? Note { get; set; }

    /// <summary>Target duration in seconds.</summary>
[System.Text.Json.Serialization.JsonIgnore]
    public double TargetDurationSec => Math.Max(0, (TargetEndMs - TargetStartMs) / 1000.0);
}
