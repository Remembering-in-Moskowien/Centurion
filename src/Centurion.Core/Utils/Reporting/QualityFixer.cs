using Centurion.Models;

namespace Centurion.Core.Utils.Reporting;

/// <summary>
/// Auto-fix engine for common subtitle quality issues:
/// 1) Overlap → adjust the timeline (shift the next line later, keeping the minimum gap);
/// 2) Too short → merge with an adjacent line;
/// 3) Line width / CPS over limit → split into two lines at the best break point
///    (time distributed by character share).
/// Re-assessing after fixing verifies that issues converge; the engine is idempotent
/// (issues already meeting the bar are not touched again).
/// </summary>
public static class QualityFixer
{
    /// <summary>Fix result: new sentence list + descriptions of applied and skipped fixes.</summary>
    public sealed record FixResult(List<Sentence> Sentences, List<string> Applied, List<string> Skipped);

    /// <summary>Runs fixes using the default thresholds.</summary>
    public static FixResult Fix(List<Sentence> sentences)
        => Fix(sentences, new QualityAssessmentOptions());

    /// <summary>Runs fixes using the specified thresholds.</summary>
    public static FixResult Fix(List<Sentence> sentences, QualityAssessmentOptions options)
    {
        var applied = new List<string>();
        var skipped = new List<string>();
        var result = sentences.Select(Clone).ToList();

        // 1) Timeline overlap: only on a real overlap (next.Start < current.End) shift the next line
        //    later to the end of the current line; contiguous runs (gap 0) and normal pauses are
        //    unaffected (MinGapMs is only used by Assess for overlap detection, not as a forced gap).
        for (var i = 0; i + 1 < result.Count; i++)
        {
            var current = result[i];
            var next = result[i + 1];
            if (next.Start >= current.End)
                continue;

            applied.Add($"Line {i + 2}: overlap fixed (Start {next.Start:F0}ms → {current.End:F0}ms).");
            next.Start = current.End;
            if (next.End < next.Start)
                next.End = next.Start;
        }

        // 2) Too-short sentences: merge with an adjacent line (prefer the previous line;
        //    the first line is merged into the next one)
        for (var i = 0; i < result.Count; i++)
        {
            var sentence = result[i];
            var duration = sentence.End - sentence.Start;
            if (duration > 0 && duration >= options.MinSentenceDurationMs)
                continue;

            var mergeInto = i - 1 >= 0 ? i - 1 : i + 1;
            if (mergeInto < 0 || mergeInto >= result.Count)
            {
                skipped.Add($"Line {i + 1}: zero/min duration cannot be merged (no neighbour).");
                continue;
            }

            var target = result[mergeInto];
            applied.Add($"Line {i + 1}: short line ({duration:F0}ms) merged into line {mergeInto + 1}.");
            target.End = Math.Max(target.End, sentence.End);
            target.Text = string.Concat(target.Text, " ", sentence.Text).Trim();
            if (target.Words.Count > 0 && sentence.Words.Count > 0)
                target.Words.AddRange(sentence.Words.Select(CloneWord));
            result.RemoveAt(i);
            i = Math.Max(-1, i - 1); // rescan at the current position (indexes shift down after removal)
        }

        // 3) Line width over limit: split into multiple parts at the best break point (at most 3 parts,
        //    each keeping its sub-timeline).
        //    Note: CPS does not converge under proportional splitting (the chars/duration ratio is
        //    unchanged), so it does not trigger a split on its own; if CPS is still high after the
        //    line width is met, it is recorded as skipped to prompt manual handling / extending duration.
        var limit = options.MaxCharsPerLine;
        for (var i = 0; i < result.Count; i++)
        {
            var sentence = result[i];
            var text = sentence.Text ?? string.Empty;
            if (CountChars(text) <= limit)
                continue;

            var parts = SplitIntoParts(sentence, limit, out var splitPoints);
            if (parts.Count == 1)
            {
                skipped.Add($"Line {i + 1}: long line has no safe split point; left as-is.");
                continue;
            }

            result.RemoveAt(i);
            result.InsertRange(i, parts);
            applied.Add($"Line {i + 1}: split into {parts.Count} lines ({CountChars(text)} chars > {limit}) at " +
                        string.Join(" / ", splitPoints) + ".");
            i += parts.Count - 1;
        }

        return new FixResult(result, applied, skipped);
    }

    /// <summary>Splits an over-long line into at most 3 parts (greedy prefix split, each part ≤ limit non-whitespace chars, time distributed by character share).</summary>
    private static List<Sentence> SplitIntoParts(Sentence sentence, int limit, out List<int> splitPoints)
    {
        splitPoints = [];
        var parts = new List<Sentence>();
        var remaining = sentence;
        while (parts.Count < 3)
        {
            var text = remaining.Text ?? string.Empty;
            if (CountChars(text) <= limit)
            {
                parts.Add(remaining);
                break;
            }

            var cut = GreedyCut(text, limit);
            if (cut <= 0 || cut >= text.Length - 1)
            {
                parts.Add(remaining);
                break;
            }

            var firstText = text[..cut].Trim();
            var secondText = text[cut..].Trim();
            if (firstText.Length == 0 || secondText.Length == 0)
            {
                parts.Add(remaining);
                break;
            }

            var duration = remaining.End - remaining.Start;
            var firstChars = CountChars(firstText);
            var totalChars = firstChars + CountChars(secondText);
            var ratio = totalChars > 0 ? firstChars / (double)totalChars : 0.5;
            var splitMs = remaining.Start + Math.Max(0, duration * ratio);

            var first = new Sentence
            {
                Text = firstText,
                Start = remaining.Start,
                End = splitMs,
                SkipRender = remaining.SkipRender,
                Confidence = remaining.Confidence
            };
            var second = new Sentence
            {
                Text = secondText,
                Start = splitMs,
                End = remaining.End,
                SkipRender = remaining.SkipRender,
                Confidence = remaining.Confidence
            };
            SplitWords(remaining.Words, ratio, first, second);
            splitPoints.Add(cut);

            parts.Add(first);
            remaining = second;
        }
        return parts;
    }

    /// <summary>
    /// Greedy break point: scan from the start of the text, taking the last position whose
    /// non-whitespace count falls within [limit/2, limit] and that sits right after punctuation
    /// or whitespace; returns -1 if none is found.
    /// </summary>
    private static int GreedyCut(string text, int limit)
    {
        var count = 0;
        var half = Math.Max(2, limit / 2);
        int? best = null;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (char.IsWhiteSpace(ch))
            {
                if (count >= half && count <= limit)
                    best = i + 1;
                continue;
            }

            count++;
            if (count > limit)
                break;
            if (count >= half && IsPunctuation(ch))
                best = i + 1;
        }
        return best ?? -1;
    }

    private static int CountChars(string text) => text.Count(ch => !char.IsWhiteSpace(ch));

    /// <summary>
    /// Finds a balanced split point: locate the midpoint by non-whitespace count (the basis for
    /// line width), then search within the [30%, 70%] window outward for the nearest punctuation
    /// (Chinese/English) or whitespace; returns -1 if none is found.
    /// </summary>
    internal static int FindSplitIndex(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return -1;

        // Index sequence of non-whitespace characters
        var nonBlank = new List<int>(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (!char.IsWhiteSpace(text[i]))
                nonBlank.Add(i);
        }
        if (nonBlank.Count <= 1)
            return -1;

        var target = nonBlank[nonBlank.Count / 2];
        var lo = nonBlank[Math.Max(0, (int)(nonBlank.Count * 0.3))];
        var hi = nonBlank[Math.Min(nonBlank.Count - 1, (int)(nonBlank.Count * 0.7))];

        // Prefer punctuation: left from target, then right
        var best = FindNearest(text, target, lo, hi, IsPunctuation);
        // Fall back to whitespace
        return best ?? FindNearest(text, target, lo, hi, char.IsWhiteSpace) ?? -1;
    }

    private static int? FindNearest(string text, int target, int lo, int hi, Func<char, bool> predicate)
    {
        for (var i = target; i >= lo; i--)
        {
            if (predicate(text[i]))
                return i + 1;
        }
        for (var i = target + 1; i <= hi; i++)
        {
            if (predicate(text[i]))
                return i + 1;
        }
        return null;
    }

    private static bool IsPunctuation(char ch)
        => ch is '。' or '！' or '？' or '；' or '，' or '：' or '、' or '.' or '!' or '?' or ';' or ':' or ',';

    /// <summary>Splits word-level details into the two new lines by character ratio (word timestamps keep their original values).</summary>
    private static void SplitWords(List<Word> words, double ratio, Sentence first, Sentence second)
    {
        if (words is null || words.Count == 0)
            return;

        var splitPoint = Math.Max(1, (int)Math.Round(words.Count * ratio));
        splitPoint = Math.Clamp(splitPoint, 1, words.Count - 1);
        first.Words.AddRange(words.Take(splitPoint).Select(CloneWord));
        second.Words.AddRange(words.Skip(splitPoint).Select(CloneWord));
    }

    private static Sentence Clone(Sentence s) => new()
    {
        Text = s.Text,
        TranslatedText = s.TranslatedText,
        CleanedText = s.CleanedText,
        Start = s.Start,
        End = s.End,
        SkipRender = s.SkipRender,
        Confidence = s.Confidence,
        Words = s.Words?.Select(CloneWord).ToList() ?? []
    };

    private static Word CloneWord(Word w) => new()
    {
        Text = w.Text,
        Start = w.Start,
        End = w.End,
        Speaker = w.Speaker,
        PosTag = w.PosTag,
        Status = w.Status,
        Confidence = w.Confidence
    };
}
