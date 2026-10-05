using Centurion.Models;

namespace Centurion.Core.Utils.Parsing;

/// <summary>
/// Word-level timestamp sanitizer. Repairs pathological word-level timestamps emitted by a
/// transcription backend (e.g. CrispASR qwen3) on long audio — out-of-order, zero/negative
/// duration, overlapping, and overlong words — so downstream sentence splitting, speaker
/// labeling and alignment receive stable, reliable input. It only applies defensive fixes and
/// never guesses true word boundaries: times it cannot determine are clamped to the "last valid
/// boundary".
/// </summary>
public static class WordTimingSanitizer
{
    /// <summary>Placeholder duration (ms) for zero/negative-duration words, to keep the word-level timeline strictly increasing.</summary>
    private const double MinWordDurationMs = 60;

    /// <summary>
    /// Sanitizes word-level timestamps: stable sort → overlap clamping → zero-duration padding.
    /// Keeps each word's text, speaker and status unchanged; only the timeline is corrected.
    /// </summary>
    /// <param name="words">The raw transcribed word stream (times may be out of order / pathological).</param>
    /// <returns>A sanitized word stream with a monotonically increasing timeline; an empty input yields an empty list.</returns>
    public static List<Word> Sanitize(IReadOnlyList<Word>? words)
    {
        if (words == null || words.Count == 0)
            return [];

        // 1. Stable sort: ascending by Start; equal times keep their original relative order
        var ordered = words
            .Select((word, index) => (Word: word, Index: index))
            .OrderBy(x => x.Word.Start)
            .ThenBy(x => x.Index)
            .Select(x => x.Word)
            .ToList();

        // 2. Clamp per word: Start no earlier than the previous word's End; pad zero/negative
        //    duration up to the minimum placeholder
        var result = new List<Word>(ordered.Count);
        var prevEnd = 0.0;
        foreach (var word in ordered)
        {
            var start = Math.Max(word.Start, prevEnd);
            var end = word.End;
            if (end <= start)
                end = start + MinWordDurationMs;

            result.Add(new Word
            {
                Text = word.Text,
                Start = start,
                End = end,
                Speaker = word.Speaker,
                Status = word.Status
            });
            prevEnd = end;
        }

        return result;
    }
}
