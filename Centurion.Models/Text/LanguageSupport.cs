namespace Centurion.Models.Text;

/// <summary>
/// 语言支持工具：为无空格分隔的语系（中日韩等）提供词拼接与标点集合。
/// 中文、日文、韩文不使用空格分隔词，其他语言按空格拼接。
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
}
