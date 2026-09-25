using Centurion.Abstractions.Strategy;
using Centurion.Models;
using Centurion.Models.Text;

namespace Centurion.Core.Strategy.SentenceSplit;

/// <summary>
/// 积极规则分句策略（默认档）：标点与自适应停顿构成硬断点，段内超长再按长度切分。
/// 硬断点保证短促密集对话（无说话人分割时）在真实停顿处断句，不再连成长行；
/// 均匀连续语音（无显著停顿、无标点）保持整段一句，仅在超长时按长度切分。
/// 适合短促对话、快节奏多人会话。
/// </summary>
public class AggressiveRuleSplitStrategy : RuleBasedSplitStrategyBase
{
    /// <summary>
    /// 对一组词流完成分句：标点与自适应停顿构成硬断点，段内超长再按长度切分。
    /// </summary>
    /// <param name="wordList">按时间排序的同一说话人词流。</param>
    /// <param name="options">分句长度与语言等配置选项。</param>
    /// <returns>切分得到的句子列表。</returns>
    protected override async Task<List<Sentence>> SplitGroupByPunctuation(List<Word> wordList, SplitOptions options)
    {
        var n = wordList.Count;

        // 1. 标点候选断点（词末字符为句末/从句标点；最后词后不断）
        var punctuationBreaks = new HashSet<int>();
        for (var i = 0; i < n; i++)
        {
            if (i == n - 1) continue;
            var current = wordList[i].Text;
            if (!string.IsNullOrEmpty(current) && BreakPunctuation.Contains(current[^1]))
                punctuationBreaks.Add(i);
        }

        // 2. 停顿候选断点（自适应阈值）：短促密集对话转录常无标点，
        //    依赖词间停顿识别句界；阈值 = max(基线, 中位间隙 × 系数)，随分句粒度调节
        var pauseBreaks = ComputePauseBreaks(wordList, options);

        // 3. 硬断点 = 标点 ∪ 停顿
        var hardBreaks = new HashSet<int>(punctuationBreaks);
        for (var i = 0; i < n; i++)
            if (pauseBreaks[i])
                hardBreaks.Add(i);

        var maxLen = options.MaxLength;

        // 4. 按硬断点分段；段内超长时再按长度切分
        var sentences = new List<Sentence>();
        var segStart = 0;
        for (var i = 0; i < n; i++)
        {
            if (i == n - 1 || hardBreaks.Contains(i))
            {
                var slice = wordList.Skip(segStart).Take(i - segStart + 1).ToList();
                if (SliceCharCount(slice) <= maxLen)
                {
                    sentences.Add(BuildSentence(slice, options));
                }
                else
                {
                    // 段内超长：无硬断点可用，按长度 DP 均匀切分
                    sentences.AddRange(SplitSegmentByLength(slice, options));
                }
                segStart = i + 1;
            }
        }

        return sentences;
    }

    /// <summary>
    /// 计算相邻词之间的显著停顿断点：间隙超过自适应阈值即视为句界。
    /// 阈值取"最大(350 − 粒度×200, 中位间隙 × (2.0 − 粒度))"毫秒——
    /// 粒度越大切分越细（阈值越低）；均匀连续语音（中位间隙与整体接近）不会触发切分。
    /// </summary>
    private static bool[] ComputePauseBreaks(List<Word> wordList, SplitOptions options)
    {
        var n = wordList.Count;
        var pauses = new bool[n];
        if (n < 2) return pauses;

        var gaps = new double[n - 1];
        for (var i = 0; i < n - 1; i++)
            gaps[i] = wordList[i + 1].Start - wordList[i].End;

        var sorted = (double[])gaps.Clone();
        Array.Sort(sorted);
        var median = sorted[sorted.Length / 2];
        var granularity = Math.Clamp(options.ChunkGranularity, 0f, 1f);
        var threshold = Math.Max(350.0 - granularity * 200.0, median * (2.0 - granularity));

        for (var i = 0; i < n - 1; i++)
        {
            // 若停顿后的下一个词以标点结尾（句末/从句标点词），该停顿多为句内换气——
            // 句界已在标点词之后，此处停顿断点会孤立句末词（导致零时长句被下游过滤丢词），故抑制
            pauses[i] = gaps[i] >= threshold && !EndsWithBreakPunctuation(wordList[i + 1].Text);
        }
        return pauses;
    }

    /// <summary>
    /// 对无硬断点可用的超长词段按长度切分：DP 在非候选位置求最优断点，
    /// 满足硬约束（每句 ≤ MaxLength）并尽量均匀接近目标长度，切分次数最少。
    /// </summary>
    private static List<Sentence> SplitSegmentByLength(List<Word> segment, SplitOptions options)
    {
        var n = segment.Count;
        if (n == 0) return [];

        var texts = segment.Select(w => w.Text).ToList();
        var lengths = texts.Select(t => t.Length).ToList();
        var sepBefore = ComputeSeparatorBefore(texts);
        var maxLen = options.MaxLength;

        const double NonCandidatePenalty = 100.0;
        const double LengthDeviationWeight = 0.05;
        const double INF = 1e9;

        var dp = new double[n + 1];
        var prev = new int[n + 1];
        dp[0] = 0;
        prev[0] = -1;

        for (var i = 1; i <= n; i++)
        {
            dp[i] = INF;
            for (var j = i - 1; j >= 0; j--)
            {
                var charSum = 0;
                for (var k = j; k < i; k++)
                    charSum += lengths[k] + (k > j ? sepBefore[k] : 0);
                if (charSum > maxLen) continue;

                var cost = (j == 0 ? 0.0 : NonCandidatePenalty)
                    + Math.Abs(charSum - options.TargetLength) * LengthDeviationWeight;
                var total = dp[j] + cost;
                if (total < dp[i])
                {
                    dp[i] = total;
                    prev[i] = j;
                }
            }
        }

        var breakPoints = new List<int>();
        var cur = n;
        while (cur > 0)
        {
            var start = prev[cur];
            breakPoints.Add(start);
            cur = start;
        }
        breakPoints.Reverse();

        var sentences = new List<Sentence>();
        for (var b = 0; b < breakPoints.Count; b++)
        {
            var startIdx = breakPoints[b];
            var endIdx = b + 1 < breakPoints.Count ? breakPoints[b + 1] : n;
            if (startIdx >= endIdx) continue;
            sentences.Add(BuildSentence(segment.Skip(startIdx).Take(endIdx - startIdx).ToList(), options));
        }
        return sentences;
    }
}
