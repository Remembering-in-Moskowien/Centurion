using System.Text;
using System.Text.RegularExpressions;

namespace Centurion.Core.Text;

public static partial class Tokenizer
{
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