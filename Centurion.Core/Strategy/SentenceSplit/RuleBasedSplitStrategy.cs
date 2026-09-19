using Centurion.Models.Ass;
using Centurion.Abstractions.Strategy;
using Centurion.Models;
using Centurion.Core.Utils;
using Centurion.Models.Text;

namespace Centurion.Core.Strategy.SentenceSplit;

/// <summary>
/// 基于标点优先的规则分句策略，支持说话人感知与中日韩（CJK）无空格语系。
/// 强制以句子结束标点（. ! ? 和 。！？）和从句标点（, ; : 和 ，；：）作为最高优先级断点，
/// 仅在无合适标点或标点导致超长时才允许在非标点位置断句。
/// 若词流携带说话人标签（说话人分割已启用），相邻词说话人切换处强制断句，
/// 同一说话人的连续词优先成句。允许更大的长度偏移（目标长度仅作为软参考）。
/// 句子文本按语言拼接：中文/日文/韩文无空格，其他语言以空格连接。
/// </summary>
public class RuleBasedSplitStrategy : BaseSplitStrategy
{
    // 所有可作为断点的标点符号（拉丁语系 + 中日韩及南亚/阿拉伯常见句读）
    private static readonly HashSet<char> BreakPunctuation =
        ['.', '!', '?', ',', ';', ':', .. LanguageSupport.CjkBreakPunctuation];

    /// <summary>说话人分割未命中时的回退标签，不视为真实说话人。</summary>
    private const string UnknownSpeaker = "SPEAKER_00";

    /// <summary>
    /// 说话人感知分句：先按说话人切换把词流强制分组，再对每组用动态规划
    /// 在标点断点候选集上求解最优断句，优先句末/从句标点，
    /// 仅在超长或无标点时才允许非标点位置断句。
    /// </summary>
    /// <param name="words">待切分的词流。</param>
    /// <param name="options">分句长度与语言等配置选项。</param>
    /// <returns>切分得到的句子列表。</returns>
    public override async Task<List<Sentence>> Split(List<Word> words, SplitOptions options)
    {
        if (words == null || words.Count == 0)
            return [];

        // 1. 按时间顺序排列单词
        var wordList = words.OrderBy(w => w.Start).ToList();

        // 2. 说话人感知：相邻词说话人切换处强制断句（同一说话人连续词成组）
        var groups = SplitBySpeaker(wordList);

        var sentences = new List<Sentence>();
        foreach (var group in groups)
            sentences.AddRange(await SplitGroupByPunctuation(group, options));

        return sentences;
    }

    /// <summary>
    /// 按说话人切换把词流强制分组：相邻两个词均携带有效说话人标签且标签不同 → 分组边界。
    /// 未标注（回退标签/空）的词不触发切分，也不阻断组内连续。
    /// </summary>
    /// <param name="words">按时间排序的词流。</param>
    /// <returns>按说话人切换分组后的词流列表。</returns>
    private static List<List<Word>> SplitBySpeaker(List<Word> words)
    {
        var groups = new List<List<Word>>();
        var current = new List<Word> { words[0] };

        for (var i = 1; i < words.Count; i++)
        {
            var prevSpeaker = words[i - 1].Speaker;
            var curSpeaker = words[i].Speaker;
            if (IsRealSpeaker(prevSpeaker) && IsRealSpeaker(curSpeaker) &&
                !string.Equals(prevSpeaker, curSpeaker, StringComparison.Ordinal))
            {
                groups.Add(current);
                current = [words[i]];
            }
            else
            {
                current.Add(words[i]);
            }
        }

        if (current.Count > 0)
            groups.Add(current);

        return groups;
    }

    /// <summary>
    /// 判断是否为真实说话人标签：非空且不是说话人分割未命中时的回退标签。
    /// </summary>
    /// <param name="speaker">词携带的说话人标签。</param>
    /// <returns>是真实说话人标签时为 true。</returns>
    private static bool IsRealSpeaker(string? speaker) =>
        !string.IsNullOrWhiteSpace(speaker) &&
        !string.Equals(speaker, UnknownSpeaker, StringComparison.Ordinal);

    /// <summary>
    /// 对单一说话人词组用动态规划在标点断点候选集上求解最优断句。
    /// </summary>
    /// <param name="wordList">按时间排序的同一说话人词流。</param>
    /// <param name="options">分句长度与语言等配置选项。</param>
    /// <returns>切分得到的句子列表。</returns>
    private async Task<List<Sentence>> SplitGroupByPunctuation(List<Word> wordList, SplitOptions options)
    {
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

        // 中日韩等无空格语系：词间不计空格，长度按字符数直接累计
        var isSpaceless = Centurion.Models.Text.LanguageSupport.IsSpaceless(options.Language);

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
                // 计算当前子句的字符数（拉丁语系含单词间空格，CJK 无空格）
                var charSum = 0;
                for (var k = j; k < i; k++)
                    charSum += lengths[k] + (k > j && !isSpaceless ? 1 : 0);

                // 硬约束：长度不得超过 MaxLength
                if (charSum > maxLen)
                    continue;

                // 判断断点位置是否在候选标点之后
                // 断点位于 j-1 和 j 之间（j 是下一句起始索引），如果 j-1 是候选索引，则此断点为候选
                // 首句（j == 0）之前没有断点，不应计非候选惩罚
                var isCandidate = j == 0 || (j < n && candidateBreakIndices.Contains(j - 1));

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
            var text = LanguageSupport.JoinWords(slice.Select(w => w.Text), options.Language);
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
