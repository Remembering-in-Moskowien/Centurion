namespace Centurion.Models;

/// <summary>Sentence-level transcription unit containing source and cleaned text, timing, and word-level details.</summary>
public class Sentence
{
    /// <summary>Original sentence text.</summary>
    public string Text { get; set; } = string.Empty;
    /// <summary>Translated text, populated by the translate command; null means untranslated.</summary>
    public string? TranslatedText { get; set; }
    /// <summary>Text after cleanup, such as punctuation removal or number expansion; null when not cleaned.</summary>
    public string? CleanedText { get; set; }
    /// <summary>Sentence start time in milliseconds.</summary>
    public double Start { get; set; }
    /// <summary>Sentence end time in milliseconds.</summary>
    public double End { get; set; }
    /// <summary>Whether to skip this sentence when rendering subtitles.</summary>
    public bool SkipRender { get; set; }
    /// <summary>Word-level details for this sentence; empty when word-level timings are unavailable.</summary>
    public List<Word> Words { get; set; } = [];
    /// <summary>Sentence-level ASR confidence (0–1), aggregated from word confidence; null if not provided by the model.</summary>
    public double? Confidence { get; set; }
    /// <summary>
    /// Bound ASS style name (the dialogue row Style column). Null uses the renderer's default style.
    /// Populated when convert parses ASS and consumed when build renders ASS, allowing the Studio frontend to assign styles per line.
    /// </summary>
    public string? Style { get; set; }
    /// <summary>
    /// Sentence source identifier, such as a media filename and track number or a subtitle filename.
    /// Populated by the combine merge operator and persisted in the IR to trace each subtitle to its input source.
    /// Sentences produced by other commands have a null source.
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// Sentence speaker inferred by majority vote from word-level <see cref="Word.Speaker"/> values.
    /// Returns null when there are no word-level details or all labels are placeholders (for example, when diarization was not run).
    /// Whether to render this value in subtitles depends on workflow state (IsDiarized) and display options.
    /// </summary>
[System.Text.Json.Serialization.JsonIgnore]
    public string? Speaker
    {
        get
        {
            if (Words.Count == 0)
                return null;

            return Words
                .Select(word => word.Speaker)
                .Where(speaker => !string.IsNullOrWhiteSpace(speaker))
                .GroupBy(speaker => speaker, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(group => group.Count())
                .FirstOrDefault()?.Key;
        }
    }
}
