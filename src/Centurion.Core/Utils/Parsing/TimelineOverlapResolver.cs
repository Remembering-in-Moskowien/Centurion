using Centurion.Models;

namespace Centurion.Core.Utils.Parsing;

/// <summary>
/// Timeline overlap resolver: sorts a subtitle sentence list by start time and then removes
/// time overlaps between adjacent sentences so the output timeline is strictly monotonic
/// (a later sentence never starts before the previous one ends). It first narrows boundaries
/// using word-level timestamps; when that cannot be determined, it splits the overlap window
/// symmetrically.
/// </summary>
public static class TimelineOverlapResolver
{
    /// <summary>
    /// Resolves time overlaps in a sentence list in place: after a stable sort by start time, it
    /// scans sequentially; when adjacent sentences overlap it first narrows the previous
    /// sentence's window to the word-level content boundary, otherwise it splits the overlap
    /// interval evenly.
    /// </summary>
    /// <param name="sentences">The sentence list to resolve (its elements are reordered and their times modified in place).</param>
    /// <returns>The number of sentences whose ordering or times changed; 0 when there is no overlap.</returns>
    public static int Resolve(List<Sentence> sentences)
    {
        if (sentences is null || sentences.Count < 2)
            return 0;

        var ordered = sentences.OrderBy(sentence => sentence.Start).ToList();

        var changed = 0;
        for (var i = 1; i < ordered.Count; i++)
        {
            var prev = ordered[i - 1];
            var cur = ordered[i];

            var overlap = prev.End - cur.Start;
            if (overlap <= 0)
                continue;

            // 1) Prefer word-level content boundaries: if the previous sentence's last word and
            //    the next sentence's first word do not overlap in themselves (only the windows do)
            //    → narrow the previous sentence's end time to the next sentence's first-word start.
            var prevWordEnd = TryGetWordBoundary(prev.Words, isFirst: false);
            var curWordStart = TryGetWordBoundary(cur.Words, isFirst: true);
            if (prevWordEnd is not null && curWordStart is not null && prevWordEnd.Value <= curWordStart.Value)
            {
                var newEnd = Math.Min(prev.End, curWordStart.Value);
                if (Math.Abs(newEnd - prev.End) > 1e-6)
                {
                    prev.End = newEnd;
                    changed++;
                }

                // Word-level data already proves the content does not overlap; keep the next sentence's start unchanged.
                continue;
            }

            // 2) Fallback: split the overlap window evenly so prev.End == cur.Start
            var half = overlap / 2.0;
            prev.End -= half;
            cur.Start += half;
            changed++;

            // Defense: windows must not go negative
            if (prev.End < prev.Start)
                prev.End = prev.Start;
            if (cur.End < cur.Start)
                cur.End = cur.Start;
        }

        // Put the reordered list back into the caller's list (keep the list consistent with time order,
        // even when no times were modified)
        var needsReorder = false;
        for (var i = 0; i < sentences.Count; i++)
        {
            if (!ReferenceEquals(sentences[i], ordered[i]))
            {
                needsReorder = true;
                break;
            }
        }

        if (needsReorder)
        {
            sentences.Clear();
            sentences.AddRange(ordered);
        }

        return changed;
    }

    /// <summary>
    /// Gets a sentence's word-level time boundary: the first word's start time or the last
    /// word's end time; returns null when there are no words.
    /// </summary>
    /// <param name="words">The sentence's word list.</param>
    /// <param name="isFirst">true to take the first word's start time, false to take the last word's end time.</param>
    /// <returns>The word-level time boundary; null when there are no words to reference.</returns>
    private static double? TryGetWordBoundary(IReadOnlyList<Word>? words, bool isFirst)
    {
        if (words is null || words.Count == 0)
            return null;

        return isFirst
            ? words.Min(word => word.Start)
            : words.Max(word => word.End);
    }
}
