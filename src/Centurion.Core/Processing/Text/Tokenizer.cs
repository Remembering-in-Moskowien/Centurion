using System.Text;
using System.Text.RegularExpressions;

namespace Centurion.Core.Processing.Text;

/// <summary>
/// Text tokenization and normalization utilities: CJK characters are split one per token,
/// while Latin letters and digits are grouped into continuous runs.
/// </summary>
public static partial class Tokenizer
{
    /// <summary>
    /// Normalizes then tokenizes the text: each CJK character becomes its own token,
    /// while consecutive non-whitespace, non-CJK characters are merged into one token.
    /// </summary>
    /// <param name="text">The text to tokenize; returns an empty list when <see langword="null"/>.</param>
    /// <returns>The list of tokens produced by tokenization.</returns>
    public static IReadOnlyList<string> Tokenize(string? text)
    {
        var normalized = Normalize(text);
        var tokens = new List<string>();
        var latinBuffer = new StringBuilder();

        foreach (var character in normalized)
        {
            if (IsCjk(character))
            {
                FlushLatin(tokens, latinBuffer);
                tokens.Add(character.ToString());
            }
            else if (char.IsWhiteSpace(character))
            {
                FlushLatin(tokens, latinBuffer);
            }
            else
            {
                latinBuffer.Append(character);
            }
        }

        FlushLatin(tokens, latinBuffer);
        return tokens;
    }

    /// <summary>
    /// Applies Unicode KC normalization, lowercases the text, and replaces punctuation and runs of
    /// whitespace with a single space, then trims leading and trailing whitespace.
    /// </summary>
    /// <param name="text">The text to normalize; treated as an empty string when <see langword="null"/>.</param>
    /// <returns>The normalized text.</returns>
    public static string Normalize(string? text)
    {
        var normalized = (text ?? string.Empty).Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        normalized = PunctuationRegex().Replace(normalized, " ");
        return WhitespaceRegex().Replace(normalized, " ").Trim();
    }

    private static void FlushLatin(ICollection<string> tokens, StringBuilder buffer)
    {
        if (buffer.Length == 0)
            return;

        tokens.Add(buffer.ToString());
        buffer.Clear();
    }

    private static bool IsCjk(char character) =>
        character is >= '\u3400' and <= '\u4DBF' or >= '\u4E00' and <= '\u9FFF' or >= '\uF900' and <= '\uFAFF';

    [GeneratedRegex(@"[\p{P}\p{S}]+")]
    private static partial Regex PunctuationRegex();
    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
