using System.Text;
using System.Text.RegularExpressions;

namespace Centurion.Core.Text;

/// <summary>
/// 文本分词与归一化工具：CJK 字符按单字切分，拉丁字母与数字按连续串切分。
/// </summary>
public static partial class Tokenizer
{
    /// <summary>
    /// 先归一化再切分文本：每个 CJK 字符单独成词，连续的非空白、非 CJK 字符合并为一个词。
    /// </summary>
    /// <param name="text">待分词文本，为 <see langword="null"/> 时返回空列表。</param>
    /// <returns>切分得到的词元列表。</returns>
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
    /// 对文本做 Unicode KC 归一化、转小写，并将标点与连续空白统一替换为单个空格后去除首尾空白。
    /// </summary>
    /// <param name="text">待归一化文本，为 <see langword="null"/> 时按空串处理。</param>
    /// <returns>归一化后的文本。</returns>
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