using Centurion.Models;
using Centurion.Models.Text;

namespace Centurion.Core.Utils.Parsing;

/// <summary>
/// Subtitle text word splitter: splits a plain-text sentence into word-level units
/// (timing evenly distributed across the sentence).
/// Uses mixed-aware tokenization - English splits on whitespace, CJK splits per character,
/// and mixed Chinese-English text coexists in the same sentence; Korean tokenizes on whitespace.
/// Reused by the convert path (subtitles without \K timing) and OCR extraction, which lack
/// a word-level timestamp source.
/// </summary>
public static class SubtitleWordSplitter
{
    /// <summary>
    /// Split plain text into word-level units with mixed-aware tokenization; timing is
    /// distributed evenly across the sentence by word count.
    /// </summary>
    /// <param name="text">A plain-text sentence (may contain mixed English, Chinese, and other scripts).</param>
    /// <param name="start">Sentence start time (milliseconds).</param>
    /// <param name="end">Sentence end time (milliseconds).</param>
    /// <param name="language">Language code (kept for caller compatibility; tokenization is based on the actual characters in the text).</param>
    /// <returns>The list of word-level units (empty list when the text is empty).</returns>
    public static List<Word> SplitPlainWords(string text, double start, double end, string? language)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
            return [];

        var tokens = LanguageSupport.TokenizeMixed(trimmed);
        if (tokens.Count == 0)
            return [];

        var duration = Math.Max(0, end - start);
        var step = duration / tokens.Count;
        var words = new List<Word>(tokens.Count);
        for (var i = 0; i < tokens.Count; i++)
        {
            var wordStart = start + i * step;
            words.Add(new Word
            {
                Text = tokens[i],
                Start = wordStart,
                End = Math.Min(end, wordStart + step),
                Speaker = "UNKNOWN",
                Status = MappingStatus.Matched
            });
        }

        return words;
    }
}
