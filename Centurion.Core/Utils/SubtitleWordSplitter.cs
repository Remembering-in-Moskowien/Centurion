using Centurion.Models;
using Centurion.Models.Text;

namespace Centurion.Core.Utils;

/// <summary>
/// 字幕文本分词工具：把纯文本句子切分为词级单元（等分句内时间）。
/// 空格语系按空白分词；中日韩等无空格语系按字符切分（每字符一词）。
/// 供 convert（无 \K 字幕）与 OCR 提取等没有词级时间戳来源的路径复用。
/// </summary>
public static class SubtitleWordSplitter
{
    /// <summary>
    /// 按语言切分纯文本为词级单元，时间按词数等分句内时长。
    /// </summary>
    /// <param name="text">纯文本句子。</param>
    /// <param name="start">句内起始时间（毫秒）。</param>
    /// <param name="end">句内结束时间（毫秒）。</param>
    /// <param name="language">语言代码，决定分词方式（CJK 逐字）。</param>
    /// <returns>词级单元列表（空文本时为空列表）。</returns>
    public static List<Word> SplitPlainWords(string text, double start, double end, string? language)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
            return [];

        var tokens = LanguageSupport.IsSpaceless(language)
            ? trimmed.Select(ch => ch.ToString()).ToList()
            : trimmed.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).ToList();
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
