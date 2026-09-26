using Centurion.Abstractions.Strategy;
using Centurion.Models;

namespace Centurion.Core.Workflow.Strategy.SentenceSplit;

/// <summary>
/// 分句策略的共享抽象基类，提供按时间间隙切分的通用能力。
/// </summary>
public abstract class BaseSplitStrategy : ISentenceSplitStrategy
{
    /// <summary>
    /// 把输入词流切分为若干句子。
    /// </summary>
    /// <param name="words">待切分的词流。</param>
    /// <param name="options">分句长度与语言等配置选项。</param>
    /// <returns>切分得到的句子列表。</returns>
    public abstract Task<List<Sentence>> Split(List<Word> words, SplitOptions options);

    /// <summary>
    /// 按相邻词之间的时间间隙把词流切分为若干词组（通用辅助方法）。
    /// </summary>
    /// <param name="words">待分组的词流（按时间排序后处理）。</param>
    /// <param name="gapMs">触发切分的最小时间间隙（毫秒）。</param>
    /// <returns>按时间间隙切分得到的词组列表。</returns>
    protected List<List<Word>> SplitByTimeGap(List<Word> words, double gapMs)
    {
        if (words == null || words.Count == 0) return [];
        words = [.. words.OrderBy(w => w.Start)];
        var segments = new List<List<Word>>();
        var current = new List<Word> { words[0] };
        for (var i = 1; i < words.Count; i++)
        {
            var gap = words[i].Start - words[i - 1].End;
            if (gap > gapMs / 1000.0) // 转换为秒
            {
                segments.Add(current);
                current = [words[i]];
            }
            else
            {
                current.Add(words[i]);
            }
        }

        if (current.Any()) segments.Add(current);
        return segments;
    }
}
