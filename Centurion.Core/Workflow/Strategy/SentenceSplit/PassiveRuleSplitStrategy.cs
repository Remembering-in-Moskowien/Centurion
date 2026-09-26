using Centurion.Abstractions.Strategy;
using Centurion.Models;
using Centurion.Models.Text;

namespace Centurion.Core.Workflow.Strategy.SentenceSplit;

/// <summary>
/// 消极规则分句策略：只认标点，用全局动态规划在标点候选断点集上求解最优断句。
/// 断点只允许出现在句末/从句标点之后（非候选位置断句有高额惩罚，仅当标点导致超长时才允许），
/// 目标长度仅作软参考（允许大偏移）。适合独白、旁白、匀速朗读等均匀连续语音——
/// 不会因词间换气停顿把句子切得过碎，标点之间的内容尽量保持为完整一句。
/// </summary>
public class PassiveRuleSplitStrategy : RuleBasedSplitStrategyBase
{
    /// <summary>
    /// 对一组词流完成分句：纯标点候选 + 全局 DP 最优断点（恢复自早期版本的规则分句实现）。
    /// </summary>
    /// <param name="wordList">按时间排序的同一说话人词流。</param>
    /// <param name="options">分句长度与语言等配置选项。</param>
    /// <returns>切分得到的句子列表。</returns>
    protected override Task<List<Sentence>> SplitGroupByPunctuation(List<Word> wordList, SplitOptions options)
    {
        var n = wordList.Count;
        var texts = wordList.Select(w => w.Text).ToList();
        var lengths = texts.Select(t => t.Length).ToList();

        // 1. 构建候选断点集合（索引表示在该单词之后断句）
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
                    // 简单过滤缩写（如 "Mr."），这里简化，全部视为断点
                    candidateBreakIndices.Add(i);
                }
            }
        }

        // 2. 动态规划（DP）求解最优断点集合
        const double NonCandidatePenalty = 100.0;   // 非候选断点的高额惩罚
        const double LengthDeviationWeight = 0.05;  // 长度偏差的权重（很低，允许大偏移）

        // 混合感知长度：类 CJK 词直连，其余词间计空格（预计算前缀分隔，O(1) 增量）
        var sepBefore = ComputeSeparatorBefore(texts);

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
                // 计算当前子句的字符数（混合感知：类 CJK 词直连，其余词间计一个空格）
                var charSum = 0;
                for (var k = j; k < i; k++)
                    charSum += lengths[k] + (k > j ? sepBefore[k] : 0);

                // 硬约束：长度不得超过 MaxLength
                if (charSum > maxLen)
                    continue;

                // 判断断点位置是否在候选标点之后
                // 断点位于 j-1 和 j 之间（j 是下一句起始索引），如果 j-1 是候选索引，则此断点为候选
                // 首句（j == 0）之前没有断点，不应计非候选惩罚
                var isCandidate = j == 0 || (j < n && candidateBreakIndices.Contains(j - 1));

                // 成本计算：非候选断点高额惩罚 + 轻微长度偏差（允许大偏移）
                var cost = isCandidate ? 0.0 : NonCandidatePenalty;
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

        // 3. 回溯得到断点位置
        var breakPoints = new List<int>();
        var cur = n;
        while (cur > 0)
        {
            var start = prev[cur];
            breakPoints.Add(start);
            cur = start;
        }
        breakPoints.Reverse();

        // 4. 构建句子
        var sentences = new List<Sentence>();
        for (var b = 0; b < breakPoints.Count; b++)
        {
            var startIdx = breakPoints[b];
            var endIdx = (b + 1 < breakPoints.Count) ? breakPoints[b + 1] : n;
            if (startIdx >= endIdx) continue;

            var slice = wordList.Skip(startIdx).Take(endIdx - startIdx).ToList();
            sentences.Add(BuildSentence(slice, options));
        }

        return Task.FromResult(sentences);
    }
}
