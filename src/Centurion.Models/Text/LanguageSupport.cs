namespace Centurion.Models.Text;

/// <summary>
/// Language support utilities: provides word joining and punctuation sets for spaceless languages (CJK, etc.),
/// and supports tokenization and joining aware of code-switching text such as mixed Chinese-English.
/// Chinese and Japanese do not separate words with spaces; other languages are joined with spaces.
/// </summary>
public static class LanguageSupport
{
    /// <summary>
    /// Whether the language is a spaceless one (CJK and common variant codes).
    /// </summary>
    /// <param name="language">Language code (e.g. "en", "zh", "zh-cn", "ja", "ko"); null or whitespace is treated as non-CJK.</param>
    /// <returns>true when the language is a CJK spaceless language.</returns>
    public static bool IsSpaceless(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return false;

        return language.Trim().ToLowerInvariant() switch
        {
            // Chinese variants and Japanese: written continuously without spaces between words
            "zh" or "zh-cn" or "zh-tw" or "zh-hk" or "zh-hans" or "zh-hant" or "chs" or "cht" or "cmn" or "yue" => true,
            "ja" or "ja-jp" or "jp" => true,
            // Note: Korean Hangul separates words with spaces (e.g. "안녕하세요 세계") and is not a spaceless language
            _ => false
        };
    }

    /// <summary>
    /// Joins a word sequence by language: CJK languages are concatenated directly (no spaces); other languages are joined with spaces.
    /// </summary>
    /// <param name="words">The word sequence to join.</param>
    /// <param name="language">Language code; determines whether spaces are inserted.</param>
    /// <returns>The joined string.</returns>
    public static string JoinWords(IEnumerable<string> words, string? language)
    {
        ArgumentNullException.ThrowIfNull(words);

        return IsSpaceless(language)
            ? string.Concat(words)
            : string.Join(" ", words);
    }

    /// <summary>
    /// Common CJK sentence-final and clause punctuation, plus South Asian (Devanagari danda । ॥) and Arabic question mark (؟).
    /// </summary>
    public static readonly char[] CjkBreakPunctuation =
        ['。', '！', '？', '，', '；', '：', '、', '…', '।', '॥', '؟'];

    /// <summary>
    /// Whether a character is a CJK ideograph / Japanese kana / Zhuyin symbol (excluding Hangul, since Korean is space-tokenized).
    /// </summary>
    /// <param name="c">The character to test.</param>
    /// <returns>true when the character is a CJK per-character writing character.</returns>
    public static bool IsCjkIdeograph(char c)
    {
        // Common ranges: Extension A, Unified Ideographs, Compatibility Ideographs, Kana, Zhuyin
        if (c is >= '\u3400' and <= '\u4DBF' or >= '\u4E00' and <= '\u9FFF'
            or >= '\uF900' and <= '\uFAFF' or >= '\u3040' and <= '\u30FF'
            or >= '\u3100' and <= '\u312F' or >= '\u31F0' and <= '\u31FF')
            return true;

        // Characters from Extension B onward appear as surrogate pairs (code points >= U+20000)
        if (char.IsSurrogate(c) && c is >= '\uD840' and <= '\uD87F')
            return true;

        return false;
    }

    /// <summary>Common CJK punctuation (punctuation marks, quotes, brackets, etc.).</summary>
    private static readonly HashSet<char> CjkPunctuationSet =
        [.. CjkBreakPunctuation, '「', '」', '『', '』', '《', '》', '（', '）', '【', '】', '〃', '々', 'ー'];

    /// <summary>
    /// Whether a character is CJK punctuation.
    /// </summary>
    /// <param name="c">The character to test.</param>
    /// <returns>true when the character is CJK punctuation.</returns>
    public static bool IsCjkPunctuation(char c) => CjkPunctuationSet.Contains(c);

    /// <summary>
    /// Whether a word token is a CJK-like word: composed entirely of CJK ideographs/kana/punctuation and Arabic digits.
    /// When joining, no space is inserted between two adjacent CJK-like words; tokens containing Latin letters are not considered CJK-like.
    /// </summary>
    /// <param name="token">The word token.</param>
    /// <returns>true when the token is a CJK-like word.</returns>
    public static bool IsCjkToken(string? token)
    {
        if (string.IsNullOrEmpty(token))
            return false;

        foreach (var c in token)
        {
            if (!IsCjkIdeograph(c) && !IsCjkPunctuation(c) && !char.IsAsciiDigit(c))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Mixed-aware tokenization: splits code-switching text such as mixed Chinese-English by character category:
    /// CJK ideographs/kana are split into single-character tokens; Latin words are split into whole words by whitespace;
    /// CJK punctuation is attached to the end of the immediately preceding CJK word (or Latin word) to avoid being isolated;
    /// Korean Hangul is tokenized by whitespace (Korean is written with spaces).
    /// For example, interleaving CJK characters between Latin words yields one token per CJK character while Latin words stay whole.
    /// </summary>
    /// <param name="text">The mixed text.</param>
    /// <returns>The mixed tokenization result (preserving original order, without whitespace).</returns>
    public static List<string> TokenizeMixed(string text)
    {
        var tokens = new List<string>();
        var latinBuffer = new System.Text.StringBuilder();

        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                FlushLatin();
                continue;
            }

            if (IsCjkIdeograph(c))
            {
                FlushLatin();
                tokens.Add(c.ToString());
                continue;
            }

            if (IsCjkPunctuation(c))
            {
                // Attach to the immediately preceding CJK word; if a Latin buffer is active, append to its tail; otherwise form its own token
                if (tokens.Count > 0 && IsCjkToken(tokens[^1]))
                    tokens[^1] += c;
                else
                    latinBuffer.Append(c);
                continue;
            }

            latinBuffer.Append(c);
        }

        FlushLatin();
        return tokens;

        void FlushLatin()
        {
            if (latinBuffer.Length > 0)
            {
                tokens.Add(latinBuffer.ToString());
                latinBuffer.Clear();
            }
        }
    }

    /// <summary>
    /// Mixed-aware joining: two adjacent CJK-like words are concatenated directly (no space); other words are joined with spaces.
    /// For example, adjacent CJK-like tokens are joined without a space, while a CJK-like token next to a Latin word gets a space.
    /// </summary>
    /// <param name="tokens">The word sequence.</param>
    /// <returns>The joined string.</returns>
    public static string JoinMixed(IEnumerable<string> tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        var sb = new System.Text.StringBuilder();
        string? prev = null;
        foreach (var token in tokens)
        {
            if (sb.Length > 0)
                sb.Append(prev is not null && IsCjkToken(prev) && IsCjkToken(token) ? string.Empty : " ");
            sb.Append(token);
            prev = token;
        }

        return sb.ToString();
    }
}
