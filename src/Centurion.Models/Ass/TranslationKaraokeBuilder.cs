using System.Text;
using Centurion.Models.Text;

namespace Centurion.Models.Ass;

/// <summary>
/// Builds word-level karaoke timings for translated sentences when source-language word alignment is unavailable.
/// Interpolates ASS \K karaoke tags (centiseconds) across the translation.
/// Reserves a lead-in pause at the start, then distributes the remaining duration by token weight;
/// longer syllables receive more time, matching the \K rhythm used in Theme.ass.
/// Output format: {\Klead-in}{\Kduration1}token1{\Kduration2}token2...
/// </summary>
public static class TranslationKaraokeBuilder
{
    /// <summary>
    /// Builds word-level \K timing text for a translation.
    /// </summary>
    /// <param name="text">Translated text.</param>
    /// <param name="startMs">Sentence start time in milliseconds.</param>
    /// <param name="endMs">Sentence end time in milliseconds.</param>
    /// <param name="language">Target language code, retained for compatibility; tokenization follows the actual text characters.</param>
    /// <returns>ASS text with \K tags.</returns>
    public static string Build(string? text, double startMs, double endMs, string? language)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var tokens = Tokenize(text);
        if (tokens.Count == 0)
            return text;

        var totalMs = Math.Max(1.0, endMs - startMs);
        var leadMs = (int)Math.Clamp(totalMs * 0.12, 300, 1000);
        var availableMs = Math.Max(1.0, totalMs - leadMs);

        var weights = tokens.Select(WeightOf).ToList();
        var weightSum = Math.Max(1, weights.Sum());

        var sb = new StringBuilder();
        sb.Append("{\\K").Append(Math.Max(1, (int)Math.Round(leadMs / 10.0))).Append('}');
        for (var i = 0; i < tokens.Count; i++)
        {
            var durationMs = Math.Max(1.0, availableMs * weights[i] / weightSum);
            sb.Append("{\\K").Append(Math.Max(1, (int)Math.Round(durationMs / 10.0))).Append('}')
              .Append(tokens[i]);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Mixed-script-aware tokenization: split CJK ideographs and kana character by character, split Latin text on whitespace, and support code-switching.
    /// Attach CJK punctuation to the preceding token.
    /// </summary>
    /// <param name="text">Translated text.</param>
    /// <returns>Token list.</returns>
    internal static List<string> Tokenize(string text) => LanguageSupport.TokenizeMixed(text);

    /// <summary>
    /// Token weight: each CJK ideograph or kana character has weight 1;
    /// Latin text is estimated by syllables (vowel-group count, minimum 1), giving longer words more time.
    /// </summary>
    /// <param name="token">A token or character.</param>
    /// <returns>Weight, at least 1.</returns>
    internal static int WeightOf(string token)
    {
        if (token.Any(LanguageSupport.IsCjkIdeograph))
            return Math.Max(1, token.Length);

        // Estimate syllables by counting vowel groups ("adventure" -> 4, "strength" -> 1).
        var syllableCount = 0;
        var inVowelRun = false;
        foreach (var c in token)
        {
            if ("aeiouyAEIOUY".Contains(c))
            {
                if (!inVowelRun)
                {
                    syllableCount++;
                    inVowelRun = true;
                }
            }
            else
            {
                inVowelRun = false;
            }
        }

        return Math.Max(1, syllableCount);
    }
}
