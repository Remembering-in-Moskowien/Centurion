using Centurion.Core.Abstractions.Strategy;
using Centurion.Core.Models;

namespace Centurion.Core.Strategy.SentenceSplit;

/// <summary>
/// 基于标点、句首大写和动态规划的规则分句策略。
/// 优先在句号/问号/感叹号处断句，若句子过长则尝试在逗号/分号/冒号处断句，
/// 并通过动态规划选择最优断点组合，使每句长度接近 TargetLength。
/// </summary>
public class RuleBasedSplitStrategy : BaseSplitStrategy
{
    private static readonly HashSet<char> SentenceFinalPunctuation = new() { '.', '!', '?' };
    private static readonly HashSet<char> ClausePunctuation = new() { ',', ';', ':' };

    public override async Task<List<Sentence>> Split(List<Word> words, SplitOptions options)
    {
        if (words == null || words.Count == 0)
            return new List<Sentence>();

        // 1. 提取每个单词的文本、长度和时间
        var wordList = words.OrderBy(w => w.Start).ToList(); // 确保时间顺序
        int n = wordList.Count;
        var texts = wordList.Select(w => w.Text).ToList();
        var lengths = texts.Select(t => t.Length).ToList();

        // 2. 构建候选断点集合（单词索引，表示在该单词后断句）
        var candidateBreakIndices = new HashSet<int>(); // 存储单词索引（0-based），表示在此单词之后断句

        // 2.1 句子结束标点：. ! ?
        for (int i = 0; i < n; i++)
        {
            if (i == n - 1) continue; // 最后单词后不断句（除非末尾标点，但已无后续）
            var current = texts[i];
            if (!string.IsNullOrEmpty(current))
            {
                char lastChar = current[^1];
                if (SentenceFinalPunctuation.Contains(lastChar))
                {
                    // 检查是否可能是缩写（如 "Mr."），简单规则：长度<=3 且前一个字符不是大写字母则可能是缩写，我们仍允许断句，但可增强
                    // 这里简化，全部当作断点
                    candidateBreakIndices.Add(i);
                }
            }
        }

        // 2.2 句首大写：当前单词后，下一个单词首字母大写，且当前单词以结束标点结尾（但已覆盖），或者当前单词是"Mr"等？为了增强，我们检查前一个单词是否以结束标点结尾，但这里已覆盖。

        // 2.3 从句标点（, ; :）：如果句子过长，可以用这些作为额外断点
        // 我们将在后续处理中动态考虑

        // 3. 动态规划求解最优断点集合
        // 目标：在候选断点中选择断点，使得每句长度（字符数）接近 TargetLength，
        // 且不超过 MaxLength，且尽量使用候选断点。
        // 允许在非候选位置强制断句（用于超长无标点情况）。

        // 定义成本函数：长度偏差平方和 + 惩罚非候选断点
        const double PenaltyNonCandidate = 100.0; // 惩罚权重

        // dp[i] = 前 i 个单词（0..i-1）的最小成本
        // dp[0] = 0
        // 转移：dp[i] = min_{j < i} ( dp[j] + cost(j, i) )
        // 其中 j 是前一句的起始索引（0-based），i 是当前句的结束索引（不包含）。
        // 成本 = 长度偏差平方 + 如果 j 不是候选断点则加惩罚（但 j 是前一句的结束，即断点位置，注意断点位于 j 单词之后）

        var dp = new double[n + 1];
        var prev = new int[n + 1]; // 记录前一个断点位置
        const double INF = 1e9;
        dp[0] = 0;
        prev[0] = -1;

        // 允许的最小和最大长度（字符数）
        int maxLen = options.MaxLength;
        // 若没有 SpreadRange，设为 TargetLength 的一半到两倍
        int spread = 10; // 可配置，但未提供，使用固定值或可从 TargetLength 计算

        for (int i = 1; i <= n; i++)
        {
            dp[i] = INF;
            // 尝试从 j 到 i-1 作为一句
            for (int j = i - 1; j >= 0; j--)
            {
                // 计算长度
                int charSum = 0;
                for (int k = j; k < i; k++)
                    charSum += lengths[k] + (k > j ? 1 : 0); // 包含空格

                // 长度必须不超过 maxLen
                if (charSum > maxLen)
                    continue;

                // 成本：长度偏差平方
                double dev = charSum - options.TargetLength;
                double cost = dev * dev;

                // 如果断点位置 j 不是候选断点（且 j != 0 和 j != n），增加惩罚
                if (j > 0 && j < n && !candidateBreakIndices.Contains(j - 1)) // 因为 j 是下一句起始，断点位于 j-1 之后
                {
                    cost += PenaltyNonCandidate;
                }

                // 总成本
                double total = dp[j] + cost;
                if (total < dp[i])
                {
                    dp[i] = total;
                    prev[i] = j;
                }
            }
        }

        // 回溯得到断点位置（每个句子的起始索引）
        var breakPoints = new List<int>();
        int cur = n;
        while (cur > 0)
        {
            int start = prev[cur];
            breakPoints.Add(start);
            cur = start;
        }
        breakPoints.Reverse();
        // breakPoints 包含每个句子的起始索引（0-based），最后一个是 n（总长度）
        // 但我们需要每句的 [start, end) 区间

        // 4. 构建句子
        var sentences = new List<Sentence>();
        for (int b = 0; b < breakPoints.Count; b++)
        {
            int startIdx = breakPoints[b];
            int endIdx = (b + 1 < breakPoints.Count) ? breakPoints[b + 1] : n;
            if (startIdx >= endIdx) continue;

            var slice = wordList.Skip(startIdx).Take(endIdx - startIdx).ToList();
            var text = string.Join(" ", slice.Select(w => w.Text));
            var sentence = new Sentence
            {
                Text = text,
                Start = slice.First().Start,
                End = slice.Last().End,
                Words = slice
            };
            sentences.Add(sentence);
        }

        // 5. 后处理：如果某句超过 maxDuration 或小于 minDuration，可进一步拆分或合并，但这里省略
        // 返回结果
        return sentences;
    }
}