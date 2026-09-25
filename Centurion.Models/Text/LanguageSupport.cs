namespace Centurion.Models.Text;

/// <summary>
/// 语言支持工具：为无空格分隔的语系（中日韩等）提供词拼接与标点集合，
/// 并支持中英混合等代码混写（code-switching）文本的感知分词与拼接。
/// 中文、日文不使用空格分隔词，其他语言按空格拼接。
/// </summary>
public static class LanguageSupport
{
    /// <summary>
    /// 判断是否为不使用空格分隔词的语系（中日韩及常见变体写法）。
    /// </summary>
    /// <param name="language">语言代码（如 "en"、"zh"、"zh-cn"、"ja"、"ko"）；null 或空白视为非 CJK。</param>
    /// <returns>是 CJK 无空格语系时为 true。</returns>
    public static bool IsSpaceless(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return false;

        return language.Trim().ToLowerInvariant() switch
        {
            // 中文各变体与日语：连续书写，词间不使用空格
            "zh" or "zh-cn" or "zh-tw" or "zh-hk" or "zh-hans" or "zh-hant" or "chs" or "cht" or "cmn" or "yue" => true,
            "ja" or "ja-jp" or "jp" => true,
            // 注意：韩语谚文用空格分隔词（如 "안녕하세요 세계"），不属于无空格语系
            _ => false
        };
    }

    /// <summary>
    /// 按语言拼接词序列：CJK 语系直接连接（无空格），其他语系以空格连接。
    /// </summary>
    /// <param name="words">待拼接的词序列。</param>
    /// <param name="language">语言代码，决定是否插入空格。</param>
    /// <returns>拼接后的字符串。</returns>
    public static string JoinWords(IEnumerable<string> words, string? language)
    {
        ArgumentNullException.ThrowIfNull(words);

        return IsSpaceless(language)
            ? string.Concat(words)
            : string.Join(" ", words);
    }

    /// <summary>
    /// 常见的中日韩句末与从句标点，外加南亚（梵文句号 । ॥）与阿拉伯问号（؟）。
    /// </summary>
    public static readonly char[] CjkBreakPunctuation =
        ['。', '！', '？', '，', '；', '：', '、', '…', '।', '॥', '؟'];

    /// <summary>
    /// 判断字符是否为中日韩表意文字/日文假名/注音符号（不含谚文——韩语按空格分词）。
    /// </summary>
    /// <param name="c">待判定字符。</param>
    /// <returns>是 CJK 逐字书写字符时为 true。</returns>
    public static bool IsCjkIdeograph(char c)
    {
        // 常用区段：扩展 A、统一表意文字、兼容表意、假名、注音
        if (c is >= '\u3400' and <= '\u4DBF' or >= '\u4E00' and <= '\u9FFF'
            or >= '\uF900' and <= '\uFAFF' or >= '\u3040' and <= '\u30FF'
            or >= '\u3100' and <= '\u312F' or >= '\u31F0' and <= '\u31FF')
            return true;

        // 扩展 B 及以后的字符以代理对形式出现（码点 ≥ U+20000）
        if (char.IsSurrogate(c) && c is >= '\uD840' and <= '\uD87F')
            return true;

        return false;
    }

    /// <summary>常见的中日韩标点（句读、引号、括号等）。</summary>
    private static readonly HashSet<char> CjkPunctuationSet =
        [.. CjkBreakPunctuation, '「', '」', '『', '』', '《', '》', '（', '）', '【', '】', '〃', '々', 'ー'];

    /// <summary>
    /// 判断字符是否为中日韩标点。
    /// </summary>
    /// <param name="c">待判定字符。</param>
    /// <returns>是中日韩标点时为 true。</returns>
    public static bool IsCjkPunctuation(char c) => CjkPunctuationSet.Contains(c);

    /// <summary>
    /// 判断词 token 是否为"类 CJK 词"：全部由 CJK 表意/假名/标点及阿拉伯数字组成。
    /// 拼接时相邻的两个类 CJK 词之间不加空格；含拉丁字母的 token 不视为类 CJK。
    /// </summary>
    /// <param name="token">词 token。</param>
    /// <returns>类 CJK 词时为 true。</returns>
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
    /// 混合感知分词：中英混合等代码混写文本按字符类别切分——
    /// CJK 表意/假名逐字拆为单字符词；拉丁语词按空白切分为整词；
    /// CJK 标点附着到紧邻的前一个 CJK 词（或拉丁词）末尾，避免被孤立；
    /// 韩语谚文按空白分词（韩语空格书写）。
    /// 例如 "hello 世界 world" → hello / 世 / 界 / world；"我们talk about" → 我 / 们 / talk / about。
    /// </summary>
    /// <param name="text">混合文本。</param>
    /// <returns>混合分词结果（保留原词序，不含空白）。</returns>
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
                // 附着到紧邻的前一个 CJK 词；若当前有拉丁缓冲则并入词尾；否则独立成词
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
    /// 混合感知拼接：相邻两个"类 CJK 词"直接相连（无空格），其余词间以空格连接。
    /// 例如 [我, 们, talk] → "我们 talk"；[hello, 世, 界] → "hello 世界"。
    /// </summary>
    /// <param name="tokens">词序列。</param>
    /// <returns>拼接后的字符串。</returns>
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
