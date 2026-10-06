using Centurion.Abstractions.Strategy;
using Centurion.Models;
using Centurion.Models.Text;

namespace Centurion.Core.Workflow.Strategy.SentenceSplit;

/// <summary>
/// Passive rule-based splitting strategy: only punctuation is honored, and global DP solves the optimal breaks over the punctuation-candidate set.
/// Breaks are allowed only after sentence/clause punctuation (breaking at non-candidate positions carries a heavy penalty, allowed only when punctuation causes over-length);
/// target length is a soft reference only (large deviation allowed). Suited to monologue, narration, and steady reading;
/// it avoids over-fragmenting sentences on inter-word breathing pauses, keeping content between punctuation as one sentence.
/// </summary>
public class PassiveRuleSplitStrategy : RuleBasedSplitStrategyBase
{
    /// <summary>
    /// Splits a word stream: pure punctuation candidates + global DP optimal breaks (restored from an earlier rule-splitting implementation).
    /// </summary>
    /// <param name="wordList">The word stream of a single speaker, ordered by time.</param>
    /// <param name="options">Configuration options such as split length and language.</param>
    /// <returns>The list of sentences after splitting.</returns>
    protected override Task<List<Sentence>> SplitGroupByPunctuation(List<Word> wordList, SplitOptions options)
    {
        var n = wordList.Count;
        var texts = wordList.Select(w => w.Text).ToList();
        var lengths = texts.Select(t => t.Length).ToList();

        // 1. Build the candidate break set (index means a break after that word)
        var candidateBreakIndices = new HashSet<int>();
        for (var i = 0; i < n; i++)
        {
            if (i == n - 1) continue; // no break after the last word
            var current = texts[i];
            if (!string.IsNullOrEmpty(current))
            {
                var lastChar = current[^1];
                if (BreakPunctuation.Contains(lastChar))
                {
                    // Simplified filtering of abbreviations (e.g. "Mr."); here all are treated as breaks
                    candidateBreakIndices.Add(i);
                }
            }
        }

        // 2. Dynamic programming (DP) to solve the optimal break set
        const double NonCandidatePenalty = 100.0;   // heavy penalty for a non-candidate break
        const double LengthDeviationWeight = 0.05;  // weight for length deviation (very low, allows large offset)

        // Mixed-aware length: CJK-like words join directly, others count a space between words (precomputed prefix separators, O(1) incremental)
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
            // Try taking j through i-1 as one sentence
            for (var j = i - 1; j >= 0; j--)
            {
                // Compute the char count of the current clause (mixed-aware: CJK-like words join directly, others count one space)
                var charSum = 0;
                for (var k = j; k < i; k++)
                    charSum += lengths[k] + (k > j ? sepBefore[k] : 0);

                // Hard constraint: length must not exceed MaxLength
                if (charSum > maxLen)
                    continue;

                // Check whether the break position is after a candidate punctuation
                // The break lies between j-1 and j (j is the start index of the next sentence); if j-1 is a candidate index, this break is a candidate
                // The first sentence (j == 0) has no preceding break, so no non-candidate penalty applies
                var isCandidate = j == 0 || (j < n && candidateBreakIndices.Contains(j - 1));

                // Cost: heavy penalty for non-candidate breaks plus a mild length deviation (large offset allowed)
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

        // 3. Backtrack to obtain the break positions
        var breakPoints = new List<int>();
        var cur = n;
        while (cur > 0)
        {
            var start = prev[cur];
            breakPoints.Add(start);
            cur = start;
        }
        breakPoints.Reverse();

        // 4. Build the sentences
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
