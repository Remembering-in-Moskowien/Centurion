using Centurion.Core.Models;

namespace Centurion.Core.Abstractions.Strategy;

public interface ISentenceSplitStrategy
{
    Task<List<Sentence>> Split(List<Word> words, SplitOptions options);
}

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

public class SplitOptions
{
    public int MaxLength { get; set; } = 80; // 字符数上限
    public int TargetLength { get; set; } = 50; // 目标字符数
    public double MaxDuration { get; set; } = 8.0; // 最大持续秒数
    public double MinDuration { get; set; } = 0.8; // 最小持续秒数
    public int MaxWordsPerLine { get; set; } = 12; // 单词数上限
    public double MergeGap { get; set; } = 1.5; // 合并短句的时间间隙（秒）
    public bool EnablePunctuationRewrite { get; set; } = true;
    public string Language { get; set; } = "en";
    public string ModelCachePath { get; set; } = string.Empty;
    public int SpreadRange { get; set; }
    public float ChunkGranularity { get; set; } = 0.5f;
    public bool EnableResegmentation { get; set; } = false;
}