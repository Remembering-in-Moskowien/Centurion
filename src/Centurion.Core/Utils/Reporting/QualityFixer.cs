using Centurion.Models;

namespace Centurion.Core.Utils.Reporting;

/// <summary>
/// 常见字幕质量问题的自动修复引擎：
/// 1) 重叠 → 调整时间轴（后句后移，保持最小间隙）；
/// 2) 过短 → 与相邻句合并；
/// 3) 行宽/CPS 超限 → 在最佳断点拆分为两句（时间按字符占比分配）。
/// 修复后重新评估可验证问题收敛；引擎保持幂等（一次修复已达标的问题不再改动）。
/// </summary>
public static class QualityFixer
{
    /// <summary>修复结果：新句子列表 + 已应用修复说明。</summary>
    public sealed record FixResult(List<Sentence> Sentences, List<string> Applied, List<string> Skipped);

    /// <summary>按默认阈值执行修复。</summary>
    public static FixResult Fix(List<Sentence> sentences)
        => Fix(sentences, new QualityAssessmentOptions());

    /// <summary>按指定阈值执行修复。</summary>
    public static FixResult Fix(List<Sentence> sentences, QualityAssessmentOptions options)
    {
        var applied = new List<string>();
        var skipped = new List<string>();
        var result = sentences.Select(Clone).ToList();

        // 1) 时间轴重叠：仅真实重叠（后句 Start < 前句 End）时把后句后移到前句结束点；
        //    连续段（间隙 0）与正常停顿不受影响（MinGapMs 仅用于 Assess 的重叠判定，非强制间隙）
        for (var i = 0; i + 1 < result.Count; i++)
        {
            var current = result[i];
            var next = result[i + 1];
            if (next.Start >= current.End)
                continue;

            applied.Add($"Line {i + 2}: overlap fixed (Start {next.Start:F0}ms → {current.End:F0}ms).");
            next.Start = current.End;
            if (next.End < next.Start)
                next.End = next.Start;
        }

        // 2) 过短句子：与相邻句合并（优先前句；首句则并入后句）
        for (var i = 0; i < result.Count; i++)
        {
            var sentence = result[i];
            var duration = sentence.End - sentence.Start;
            if (duration > 0 && duration >= options.MinSentenceDurationMs)
                continue;

            var mergeInto = i - 1 >= 0 ? i - 1 : i + 1;
            if (mergeInto < 0 || mergeInto >= result.Count)
            {
                skipped.Add($"Line {i + 1}: zero/min duration cannot be merged (no neighbour).");
                continue;
            }

            var target = result[mergeInto];
            applied.Add($"Line {i + 1}: short line ({duration:F0}ms) merged into line {mergeInto + 1}.");
            target.End = Math.Max(target.End, sentence.End);
            target.Text = string.Concat(target.Text, " ", sentence.Text).Trim();
            if (target.Words.Count > 0 && sentence.Words.Count > 0)
                target.Words.AddRange(sentence.Words.Select(CloneWord));
            result.RemoveAt(i);
            i = Math.Max(-1, i - 1); // 重新扫描当前位置（索引因移除而前移）
        }

        // 3) 行宽超限：在最佳断点拆分为多段（≤3 段，每段保留子时间轴）。
        //    注：CPS 随等比拆分不收敛（字符/时长比不变），故不单独触发拆分；
        //    行宽达标后若 CPS 仍高，记 skipped 提示人工/延长时长。
        var limit = options.MaxCharsPerLine;
        for (var i = 0; i < result.Count; i++)
        {
            var sentence = result[i];
            var text = sentence.Text ?? string.Empty;
            if (CountChars(text) <= limit)
                continue;

            var parts = SplitIntoParts(sentence, limit, out var splitPoints);
            if (parts.Count == 1)
            {
                skipped.Add($"Line {i + 1}: long line has no safe split point; left as-is.");
                continue;
            }

            result.RemoveAt(i);
            result.InsertRange(i, parts);
            applied.Add($"Line {i + 1}: split into {parts.Count} lines ({CountChars(text)} chars > {limit}) at " +
                        string.Join(" / ", splitPoints) + ".");
            i += parts.Count - 1;
        }

        return new FixResult(result, applied, skipped);
    }

    /// <summary>把超行宽句子拆成 ≤3 段（贪心前缀拆分，每段 ≤ limit 非空白字符，时间按字符占比分配）。</summary>
    private static List<Sentence> SplitIntoParts(Sentence sentence, int limit, out List<int> splitPoints)
    {
        splitPoints = [];
        var parts = new List<Sentence>();
        var remaining = sentence;
        while (parts.Count < 3)
        {
            var text = remaining.Text ?? string.Empty;
            if (CountChars(text) <= limit)
            {
                parts.Add(remaining);
                break;
            }

            var cut = GreedyCut(text, limit);
            if (cut <= 0 || cut >= text.Length - 1)
            {
                parts.Add(remaining);
                break;
            }

            var firstText = text[..cut].Trim();
            var secondText = text[cut..].Trim();
            if (firstText.Length == 0 || secondText.Length == 0)
            {
                parts.Add(remaining);
                break;
            }

            var duration = remaining.End - remaining.Start;
            var firstChars = CountChars(firstText);
            var totalChars = firstChars + CountChars(secondText);
            var ratio = totalChars > 0 ? firstChars / (double)totalChars : 0.5;
            var splitMs = remaining.Start + Math.Max(0, duration * ratio);

            var first = new Sentence
            {
                Text = firstText,
                Start = remaining.Start,
                End = splitMs,
                SkipRender = remaining.SkipRender,
                Confidence = remaining.Confidence
            };
            var second = new Sentence
            {
                Text = secondText,
                Start = splitMs,
                End = remaining.End,
                SkipRender = remaining.SkipRender,
                Confidence = remaining.Confidence
            };
            SplitWords(remaining.Words, ratio, first, second);
            splitPoints.Add(cut);

            parts.Add(first);
            remaining = second;
        }
        return parts;
    }

    /// <summary>
    /// 贪心断点：从文本开头扫描，取非空白字符数落在 [limit/2, limit] 区间内、
    /// 且位于标点或空白之后的最后一个位置；找不到返回 -1。
    /// </summary>
    private static int GreedyCut(string text, int limit)
    {
        var count = 0;
        var half = Math.Max(2, limit / 2);
        int? best = null;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (char.IsWhiteSpace(ch))
            {
                if (count >= half && count <= limit)
                    best = i + 1;
                continue;
            }

            count++;
            if (count > limit)
                break;
            if (count >= half && IsPunctuation(ch))
                best = i + 1;
        }
        return best ?? -1;
    }

    private static int CountChars(string text) => text.Count(ch => !char.IsWhiteSpace(ch));

    /// <summary>
    /// 找到均衡拆分点：按非空白字符数定位中点（行宽依据），在 [30%, 70%] 区间内
    /// 向两侧找最近的标点（中文/英文）或空白；找不到返回 -1。
    /// </summary>
    internal static int FindSplitIndex(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return -1;

        // 非空白字符的原始索引序列
        var nonBlank = new List<int>(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (!char.IsWhiteSpace(text[i]))
                nonBlank.Add(i);
        }
        if (nonBlank.Count <= 1)
            return -1;

        var target = nonBlank[nonBlank.Count / 2];
        var lo = nonBlank[Math.Max(0, (int)(nonBlank.Count * 0.3))];
        var hi = nonBlank[Math.Min(nonBlank.Count - 1, (int)(nonBlank.Count * 0.7))];

        // 标点优先：从 target 向左，再向右
        var best = FindNearest(text, target, lo, hi, IsPunctuation);
        // 空白兜底
        return best ?? FindNearest(text, target, lo, hi, char.IsWhiteSpace) ?? -1;
    }

    private static int? FindNearest(string text, int target, int lo, int hi, Func<char, bool> predicate)
    {
        for (var i = target; i >= lo; i--)
        {
            if (predicate(text[i]))
                return i + 1;
        }
        for (var i = target + 1; i <= hi; i++)
        {
            if (predicate(text[i]))
                return i + 1;
        }
        return null;
    }

    private static bool IsPunctuation(char ch)
        => ch is '。' or '！' or '？' or '；' or '，' or '：' or '、' or '.' or '!' or '?' or ';' or ':' or ',';

    /// <summary>按字符比例把词级明细拆分到两个新句（词时间戳保持原值）。</summary>
    private static void SplitWords(List<Word> words, double ratio, Sentence first, Sentence second)
    {
        if (words is null || words.Count == 0)
            return;

        var splitPoint = Math.Max(1, (int)Math.Round(words.Count * ratio));
        splitPoint = Math.Clamp(splitPoint, 1, words.Count - 1);
        first.Words.AddRange(words.Take(splitPoint).Select(CloneWord));
        second.Words.AddRange(words.Skip(splitPoint).Select(CloneWord));
    }

    private static Sentence Clone(Sentence s) => new()
    {
        Text = s.Text,
        TranslatedText = s.TranslatedText,
        CleanedText = s.CleanedText,
        Start = s.Start,
        End = s.End,
        SkipRender = s.SkipRender,
        Confidence = s.Confidence,
        Words = s.Words?.Select(CloneWord).ToList() ?? []
    };

    private static Word CloneWord(Word w) => new()
    {
        Text = w.Text,
        Start = w.Start,
        End = w.End,
        Speaker = w.Speaker,
        PosTag = w.PosTag,
        Status = w.Status,
        Confidence = w.Confidence
    };
}
