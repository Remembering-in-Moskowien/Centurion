using System.Text;
using System.Text.RegularExpressions;

namespace Centurion.Core.Utils.Parsing;

/// <summary>
/// Punctuation utilities: strip punctuation, and rewrite sentence punctuation for
/// punctuation-less text based on sense-group boundaries.
/// </summary>
public static class PunctuationHandler
{
    private static readonly Regex PunctuationRegex = new(@"[^\w\s]", RegexOptions.Compiled);
    private static readonly HashSet<char> SentenceEndPunctuations = ['.', '?', '!'];
    private static readonly HashSet<char> InternalPunctuations = [',', ';', ':'];

    /// <summary>
    /// Remove all punctuation (keeping letters, digits, and whitespace).
    /// </summary>
    public static string RemoveAllPunctuation(string text)
    {
        return PunctuationRegex.Replace(text, "");
    }

    /// <summary>
    /// Rewrite punctuation for a sentence: append . or ? / ! at the end, and insert commas at sense-group boundaries inside.
    /// </summary>
    /// <param name="text">Punctuation-less text.</param>
    /// <param name="phraseBoundaries">Sense-group boundary indices (character positions).</param>
    /// <param name="isQuestion">Whether this is a question.</param>
    public static string RewritePunctuation(string text, List<int> phraseBoundaries, bool isQuestion = false)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        // Internal punctuation: insert a comma at each boundary (unless the current position is already whitespace)
        var sb = new StringBuilder(text);
        var offset = 0;
        foreach (var pos in phraseBoundaries.OrderBy(p => p))
            if (pos >= 0 && pos < sb.Length && !char.IsWhiteSpace(sb[pos - 1 + offset]))
            {
                sb.Insert(pos + offset, ", ");
                offset += 2;
            }

        // Sentence-ending punctuation
        var endPunct = isQuestion ? "?" : ".";
        // Overwrite a trailing punctuation if one already exists; otherwise append it
        var final = sb.ToString().TrimEnd();
        if (final.Length > 0)
        {
            var last = final[^1];
            if (SentenceEndPunctuations.Contains(last))
                final = final[..^1] + endPunct;
            else if (!char.IsPunctuation(last))
                final += endPunct;
        }

        return final;
    }}
