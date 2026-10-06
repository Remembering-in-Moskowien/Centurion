using Centurion.Abstractions.Strategy;
using Centurion.Models;

namespace Centurion.Core.Workflow.Strategy.SentenceSplit;

/// <summary>
/// Shared abstract base class for sentence-splitting strategies, providing the common ability to split by time gap.
/// </summary>
public abstract class BaseSplitStrategy : ISentenceSplitStrategy
{
    /// <summary>Display name of the strategy (overridden by subclasses, e.g. "sat", "llm").</summary>
    public virtual string StrategyName => "rule";

    /// <summary>Sentence-splitting strategies do not declare pipeline-level capabilities by default.</summary>
    public virtual StrategyCapabilities Capabilities => StrategyCapabilities.None;

    /// <summary>
    /// Splits the input word stream into sentences.
    /// </summary>
    /// <param name="words">The word stream to split.</param>
    /// <param name="options">Configuration options such as split length and language.</param>
    /// <returns>The list of sentences after splitting.</returns>
    public abstract Task<List<Sentence>> Split(List<Word> words, SplitOptions options);

    /// <summary>
    /// Splits the word stream into word groups by the time gap between adjacent words (shared helper).
    /// </summary>
    /// <param name="words">The word stream to group (processed after sorting by time).</param>
    /// <param name="gapMs">Minimum time gap (milliseconds) that triggers a split.</param>
    /// <returns>The list of word groups split by time gap.</returns>
    protected List<List<Word>> SplitByTimeGap(List<Word> words, double gapMs)
    {
        if (words == null || words.Count == 0) return [];
        words = [.. words.OrderBy(w => w.Start)];
        var segments = new List<List<Word>>();
        var current = new List<Word> { words[0] };
        for (var i = 1; i < words.Count; i++)
        {
            var gap = words[i].Start - words[i - 1].End;
            if (gap > gapMs / 1000.0) // convert to seconds
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
