using Centurion.Core.Abstractions.Strategy;
using Centurion.Core.Models;
using Centurion.Core.Utils;

namespace Centurion.Core.Strategy.SentenceSplit;

/// <summary>
/// 基于标点优先的规则分句策略。
/// 强制以句子结束标点（. ! ?）和从句标点（, ; :）作为最高优先级断点，
/// 仅在无合适标点或标点导致超长时才允许在非标点位置断句。
/// 允许更大的长度偏移（目标长度仅作为软参考）。
/// </summary>
public class RuleBasedSplitStrategy : BaseSplitStrategy
{
    // 所有可作为断点的标点符号（包括结束标点和从句标点）
    private static readonly HashSet<char> BreakPunctuation = new() { '.', '!', '?', ',', ';', ':' };

    public override async Task<List<Sentence>> Split(List<Word> words, SplitOptions options)
    {
        if (words == null || words.Count == 0)
            return new List<Sentence>();

        // 1. 按时间顺序排列单词
        var wordList = words.OrderBy(w => w.Start).ToList();
        var n = wordList.Count;
        var texts = wordList.Select(w => w.Text).ToList();
        var lengths = texts.Select(t => t.Length).ToList();

        // 2. 构建候选断点集合（索引表示在该单词之后断句）
        var candidateBreakIndices = new HashSet<int>();
        for (var i = 0; i < n; i++)
        {
            if (i == n - 1) continue; // 最后单词后不断句
            var current = texts[i];
            if (!string.IsNullOrEmpty(current))
            {
                var lastChar = current[^1];
                if (BreakPunctuation.Contains(lastChar))
                {
                    // 简单过滤缩写（如 "Mr."），但这里简化，全部视为断点
                    candidateBreakIndices.Add(i);
                }
            }
        }

        // 3. 动态规划（DP）求解最优断点集合
        const double NonCandidatePenalty = 100.0;   // 非候选断点的高额惩罚
        const double LengthDeviationWeight = 0.05;  // 长度偏差的权重（很低，允许大偏移）

        var dp = new double[n + 1];
        var prev = new int[n + 1];
        const double INF = 1e9;
        dp[0] = 0;
        prev[0] = -1;

        var maxLen = options.MaxLength;

        for (var i = 1; i <= n; i++)
        {
            dp[i] = INF;
            // 尝试从 j 到 i-1 作为一句
            for (var j = i - 1; j >= 0; j--)
            {
                // 计算当前子句的字符数（含单词间空格）
                var charSum = 0;
                for (var k = j; k < i; k++)
                    charSum += lengths[k] + (k > j ? 1 : 0);

                // 硬约束：长度不得超过 MaxLength
                if (charSum > maxLen)
                    continue;

                // 判断断点位置是否在候选标点之后
                // 断点位于 j-1 和 j 之间（j 是下一句起始索引），如果 j-1 是候选索引，则此断点为候选
                var isCandidate = (j > 0 && j < n && candidateBreakIndices.Contains(j - 1));

                // 成本计算
                var cost = isCandidate ? 0.0 : NonCandidatePenalty;
                // 增加轻微的长度偏差惩罚（绝对值，允许大偏移）
                double deviation = Math.Abs(charSum - options.TargetLength);
                cost += deviation * LengthDeviationWeight;

                var total = dp[j] + cost;
                if (total < dp[i])
                {
                    dp[i] = total;
                    prev[i] = j;
                }
            }
        }

        // 4. 回溯得到断点位置
        var breakPoints = new List<int>();
        var cur = n;
        while (cur > 0)
        {
            var start = prev[cur];
            breakPoints.Add(start);
            cur = start;
        }
        breakPoints.Reverse();

        // 5. 构建句子
        var sentences = new List<Sentence>();
        for (var b = 0; b < breakPoints.Count; b++)
        {
            var startIdx = breakPoints[b];
            var endIdx = (b + 1 < breakPoints.Count) ? breakPoints[b + 1] : n;
            if (startIdx >= endIdx) continue;

            var slice = wordList.Skip(startIdx).Take(endIdx - startIdx).ToList();
            var text = string.Join(" ", slice.Select(w => w.Text));
            var sentence = new Sentence
            {
                Text = SubTools.NormalizeSpaces(text),
                Start = slice.First().Start,
                End = slice.Last().End,
                Words = slice
            };
            sentences.Add(sentence);
        }

        return sentences;
    }
}