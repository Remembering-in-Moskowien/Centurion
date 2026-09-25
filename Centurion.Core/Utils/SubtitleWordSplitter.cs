using Centurion.Models;
using Centurion.Models.Text;

namespace Centurion.Core.Utils;

/// <summary>
/// 字幕文本分词工具：把纯文本句子切分为词级单元（等分句内时间）。
/// 使用混合感知分词——英文按空白拆词、中日韩逐字、中英混写共存于同一句；
/// 韩语按空白分词。供 convert（无 \K 字幕）与 OCR 提取等没有词级时间戳来源的路径复用。
/// </summary>
public static class SubtitleWordSplitter
{
    /// <summary>
    /// 按混合感知分词切分纯文本为词级单元，时间按词数等分句内时长。
    /// </summary>
    /// <param name="text">纯文本句子（可含中英等多语言混写）。</param>
    /// <param name="start">句内起始时间（毫秒）。</param>
    /// <param name="end">句内结束时间（毫秒）。</param>
    /// <param name="language">语言代码（保留用于兼容调用方；分词以文本实际字符为准）。</param>
    /// <returns>词级单元列表（空文本时为空列表）。</returns>
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
