using Centurion.Abstractions.Strategy;
using Centurion.Models;
using Centurion.Models.Text;

namespace Centurion.Core.Workflow.Strategy.SentenceSplit;

/// <summary>
/// Aggressive rule-based splitting strategy (default tier): punctuation and adaptive pauses form hard breaks,
/// and over-long segments are further split by length. Hard breaks keep short, dense dialogue (without diarization)
/// breaking at real pauses instead of long lines; uniform continuous speech (no pauses, no punctuation) stays as
/// one segment, split by length only when over-long. Suited to short exchanges and fast-paced multi-party talk.
/// </summary>
public class AggressiveRuleSplitStrategy : RuleBasedSplitStrategyBase
{
    /// <summary>
    /// Splits a word stream into sentences: punctuation and adaptive pauses form hard breaks, over-long segments are then split by length.
    /// </summary>
    /// <param name="wordList">The word stream of a single speaker, ordered by time.</param>
    /// <param name="options">Configuration options such as split length and language.</param>
    /// <returns>The list of sentences after splitting.</returns>
    protected override async Task<List<Sentence>> SplitGroupByPunctuation(List<Word> wordList, SplitOptions options)
    {
        var n = wordList.Count;

        // 1. Punctuation candidate breaks (word ends with sentence/clause punctuation; no break after the last word)
        var punctuationBreaks = new HashSet<int>();
        for (var i = 0; i < n; i++)
        {
            if (i == n - 1) continue;
            var current = wordList[i].Text;
            if (!string.IsNullOrEmpty(current) && BreakPunctuation.Contains(current[^1]))
                punctuationBreaks.Add(i);
        }

        // 2. Pause candidate breaks (adaptive threshold): transcriptions of short, dense dialogue often lack punctuation,
        //    so sentence boundaries rely on inter-word pauses; threshold = max(baseline, median gap x factor), tuned by granularity
        var pauseBreaks = ComputePauseBreaks(wordList, options);

        // 3. Hard breaks = punctuation U pauses
        var hardBreaks = new HashSet<int>(punctuationBreaks);
        for (var i = 0; i < n; i++)
            if (pauseBreaks[i])
                hardBreaks.Add(i);

        var maxLen = options.MaxLength;

        // 4. Segment by hard breaks; split over-long segments by length
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
                    // Over-long segment: no hard break available, split evenly by length DP
                    sentences.AddRange(SplitSegmentByLength(slice, options));
                }
                segStart = i + 1;
            }
        }

        return sentences;
    }

    /// <summary>
    /// Computes significant pause breaks between adjacent words: a gap exceeding the adaptive threshold counts as a boundary.
    /// The threshold is "max(350 − granularity×200, median gap × (2.0 − granularity))" ms —
    /// the higher the granularity, the finer the split (lower threshold); uniform continuous speech triggers no split.
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
            // If the word after a pause ends with punctuation (a sentence/clause-ending word), the pause is mostly in-sentence breathing —
            // the boundary already lies after that punctuation word; a pause break here would isolate the ending word (zero-duration sentences dropped downstream), so suppress it
            pauses[i] = gaps[i] >= threshold && !EndsWithBreakPunctuation(wordList[i + 1].Text);
        }
        return pauses;
    }

    /// <summary>
    /// Splits an over-long word segment with no hard break by length: DP finds the optimal breaks at non-candidate
    /// positions, satisfying the hard constraint (each sentence ≤ MaxLength) while staying even, near target length, with the fewest splits.
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
