using Centurion.Models;

namespace Centurion.Core.Utils;

/// <summary>
/// 时间轴重叠消解器：对字幕句列表按开始时间排序后消除相邻句的时间重叠，
/// 保证输出时间轴严格单调（后句开始时间不小于前句结束时间）。
/// 优先利用词级时间戳做内容边界收窄，无法判定时对称均分重叠窗口。
/// </summary>
public static class TimelineOverlapResolver
{
    /// <summary>
    /// 就地消除句子列表中的时间重叠：按开始时间稳定排序后顺序扫描，
    /// 相邻句重叠时优先按词级内容边界收窄前句窗口，否则均分重叠区间。
    /// </summary>
    /// <param name="sentences">待消解的句子列表（元素将被就地修改时间并重排）。</param>
    /// <returns>发生时间顺序调整或时间修改的句子数；无重叠时返回 0。</returns>
    public static int Resolve(List<Sentence> sentences)
    {
        if (sentences is null || sentences.Count < 2)
            return 0;

        var ordered = sentences.OrderBy(sentence => sentence.Start).ToList();

        var changed = 0;
        for (var i = 1; i < ordered.Count; i++)
        {
            var prev = ordered[i - 1];
            var cur = ordered[i];

            var overlap = prev.End - cur.Start;
            if (overlap <= 0)
                continue;

            // 1) 词级内容边界优先：前句末词与后句首词若本身不重叠，仅窗口重叠
            //    → 把前句结束时间收窄到后句首词开始处。
            var prevWordEnd = TryGetWordBoundary(prev.Words, isFirst: false);
            var curWordStart = TryGetWordBoundary(cur.Words, isFirst: true);
            if (prevWordEnd is not null && curWordStart is not null && prevWordEnd.Value <= curWordStart.Value)
            {
                var newEnd = Math.Min(prev.End, curWordStart.Value);
                if (Math.Abs(newEnd - prev.End) > 1e-6)
                {
                    prev.End = newEnd;
                    changed++;
                }

                // 词级已证明内容不重叠，后句开始时间保持原值
                continue;
            }

            // 2) 兜底：均分重叠窗口，保证 prev.End == cur.Start
            var half = overlap / 2.0;
            prev.End -= half;
            cur.Start += half;
            changed++;

            // 防御：窗口不允许为负
            if (prev.End < prev.Start)
                prev.End = prev.Start;
            if (cur.End < cur.Start)
                cur.End = cur.Start;
        }

        // 按修正后的时间重排回调用方列表（保持列表与时间顺序一致，即使无时间修改）
        var needsReorder = false;
        for (var i = 0; i < sentences.Count; i++)
        {
            if (!ReferenceEquals(sentences[i], ordered[i]))
            {
                needsReorder = true;
                break;
            }
        }

        if (needsReorder)
        {
            sentences.Clear();
            sentences.AddRange(ordered);
        }

        return changed;
    }

    /// <summary>
    /// 取句子词级时间边界：首词开始时间或末词结束时间；无词时返回 null。
    /// </summary>
    /// <param name="words">句子的词列表。</param>
    /// <param name="isFirst">true 取首词开始时间，false 取末词结束时间。</param>
    /// <returns>词级时间边界；无词可参考时返回 null。</returns>
    private static double? TryGetWordBoundary(IReadOnlyList<Word>? words, bool isFirst)
    {
        if (words is null || words.Count == 0)
            return null;

        return isFirst
            ? words.Min(word => word.Start)
            : words.Max(word => word.End);
    }
}
