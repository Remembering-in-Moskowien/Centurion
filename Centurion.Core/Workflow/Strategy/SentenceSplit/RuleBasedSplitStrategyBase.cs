using Centurion.Abstractions.Strategy;
using Centurion.Models;
using Centurion.Models.Ass;
using Centurion.Models.Text;

namespace Centurion.Core.Workflow.Strategy.SentenceSplit;

/// <summary>
/// 规则分句策略的共享抽象基类（积极/消极两档通用逻辑）。
/// 两档均强制以句子结束标点（. ! ? 和 。！？）与从句标点（, ; : 和 ，；：）为最高优先级断点，
/// 支持说话人感知（相邻词说话人切换处强制断句）与中日韩（CJK）无空格语系拼接。
/// 差异仅在断句候选的选取：积极档额外把显著词间停顿视为硬断点（适合短促密集对话），
/// 消极档只认标点、用全局动态规划在标点候选集上求最优断句（适合独白等均匀连续语音）。
/// </summary>
public abstract class RuleBasedSplitStrategyBase : BaseSplitStrategy
{
    /// <summary>所有可作为断点的标点符号（拉丁语系 + 中日韩及南亚/阿拉伯常见句读）。</summary>
    protected static readonly HashSet<char> BreakPunctuation =
        ['.', '!', '?', ',', ';', ':', .. LanguageSupport.CjkBreakPunctuation];

    /// <summary>说话人分割未命中时的回退标签，不视为真实说话人。</summary>
    private const string UnknownSpeaker = "SPEAKER_00";

    /// <summary>
    /// 说话人感知分句：先按说话人切换把词流强制分组，再对每组调用 <see cref="SplitGroupByPunctuation"/>。
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
    /// 子类实现的具体断句策略（积极档=标点+停顿硬断点；消极档=标点+全局 DP）。
    /// </summary>
    /// <param name="wordList">按时间排序的同一说话人词流。</param>
    /// <param name="options">分句长度与语言等配置选项。</param>
    /// <returns>切分得到的句子列表。</returns>
    protected abstract Task<List<Sentence>> SplitGroupByPunctuation(List<Word> wordList, SplitOptions options);

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

    /// <summary>判断是否为真实说话人标签：非空且不是说话人分割未命中时的回退标签。</summary>
    private static bool IsRealSpeaker(string? speaker) =>
        !string.IsNullOrWhiteSpace(speaker) &&
        !string.Equals(speaker, UnknownSpeaker, StringComparison.Ordinal);

    /// <summary>判断词文本是否以断句/从句标点结尾（这些词之后才是真正的句界）。</summary>
    protected static bool EndsWithBreakPunctuation(string? text) =>
        !string.IsNullOrEmpty(text) && BreakPunctuation.Contains(text[^1]);

    /// <summary>统计一段词流的显示长度（混合感知：类 CJK 词直连，其余词间计一个空格）。</summary>
    protected static int SliceCharCount(List<Word> slice) =>
        LanguageSupport.JoinMixed(slice.Select(w => w.Text)).Length;

    /// <summary>
    /// 预计算相邻词之间是否插入空格（类 CJK 词之间直连，否则一个空格），
    /// 供动态规划 O(1) 增量计算子句字符数。
    /// </summary>
    /// <param name="texts">按时间排序的词文本。</param>
    /// <returns>sepBefore[i] = 词 i 前的空格数（i=0 时为 0）。</returns>
    protected static int[] ComputeSeparatorBefore(List<string> texts)
    {
        var sepBefore = new int[texts.Count];
        for (var i = 1; i < texts.Count; i++)
        {
            sepBefore[i] = LanguageSupport.IsCjkToken(texts[i - 1]) && LanguageSupport.IsCjkToken(texts[i])
                ? 0
                : 1;
        }
        return sepBefore;
    }

    /// <summary>从词流片段构建一个句子（文本按混合感知拼接，时间取首末词）。</summary>
    protected static Sentence BuildSentence(List<Word> slice, SplitOptions options)
    {
        var text = LanguageSupport.JoinMixed(slice.Select(w => w.Text));
        return new Sentence
        {
            Text = SubTools.NormalizeSpaces(text),
            Start = slice.First().Start,
            End = slice.Last().End,
            Words = slice
        };
    }
}
