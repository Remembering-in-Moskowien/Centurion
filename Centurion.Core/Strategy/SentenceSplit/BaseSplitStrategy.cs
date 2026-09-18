using Centurion.Core.Abstractions.Strategy;
using Centurion.Core.Models;

namespace Centurion.Core.Strategy.SentenceSplit;

/// <summary>
/// 分句策略的共享抽象基类，提供按时间间隙切分的通用能力。
/// </summary>
public abstract class BaseSplitStrategy : ISentenceSplitStrategy
{
    public abstract Task<List<Sentence>> Split(List<Word> words, SplitOptions options);

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
