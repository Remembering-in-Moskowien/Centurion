using Centurion.Models;

namespace Centurion.Core.Utils.Parsing;

/// <summary>
/// 词级时间戳健康化工具。
/// 修复转录后端（如 CrispASR qwen3）在长音频上输出的病态词级时间戳——
/// 乱序、零时长/负时长、重叠、超长单词——使下游分句、说话人标注与对齐输入稳定可靠。
/// 仅做防御性修正，不猜测真实词边界：无法确定的时间保持"上一个有效边界"钳制。
/// </summary>
public static class WordTimingSanitizer
{
    /// <summary>零时长/负时长词的占位时长（毫秒），保证词级时间轴严格递增。</summary>
    private const double MinWordDurationMs = 60;

    /// <summary>
    /// 健康化词级时间戳：稳定排序 → 重叠钳制 → 零时长补位。
    /// 保持词的文本、说话人与状态不变，仅修正时间轴。
    /// </summary>
    /// <param name="words">转录原始词流（时间可能乱序/病态）。</param>
    /// <returns>时间轴单调递增的健康化词流；空输入返回空列表。</returns>
    public static List<Word> Sanitize(IReadOnlyList<Word>? words)
    {
        if (words == null || words.Count == 0)
            return [];

        // 1. 稳定排序：按 Start 升序，相同时间保持原相对顺序
        var ordered = words
            .Select((word, index) => (Word: word, Index: index))
            .OrderBy(x => x.Word.Start)
            .ThenBy(x => x.Index)
            .Select(x => x.Word)
            .ToList();

        // 2. 逐词钳制：Start 不早于前词 End；零时长/负时长补最小占位
        var result = new List<Word>(ordered.Count);
        var prevEnd = 0.0;
        foreach (var word in ordered)
        {
            var start = Math.Max(word.Start, prevEnd);
            var end = word.End;
            if (end <= start)
                end = start + MinWordDurationMs;

            result.Add(new Word
            {
                Text = word.Text,
                Start = start,
                End = end,
                Speaker = word.Speaker,
                Status = word.Status
            });
            prevEnd = end;
        }

        return result;
    }
}
