using Centurion.Abstractions.Pipeline;
using System.Text.RegularExpressions;
using Centurion.Models;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Workflow.Pipeline;

/// <summary>
/// Shared base class for timeline-alignment operators.
///
/// Provides word-level Needleman-Wunsch (NW) global alignment, sentence-level
/// aggregation, gap filling, and time-monotonicity checks, shared by
/// ScriptTimelineMapperOperator (script time-stamping) and the
/// timeline mapping & correction operator.
///
/// NW scoring (unified convention):
///   Exact match           -> 3
///   Fuzzy match (>= 0.75) -> 2
///   Mismatch              -> -1
///   Gap                   -> -1
///
/// Scale strategy: full NW -> banded NW -> sparse sliding, so large inputs never OOM.
/// </summary>
public abstract partial class TimelineAlignmentOperatorBase<TSelf> : PipelineOperatorBase<TSelf>
    where TSelf : TimelineAlignmentOperatorBase<TSelf>
{
    // ===== NW scoring =====
    /// <summary>NW diagonal score when the two words are identical.</summary>
    protected const int NwExactMatch = 3;
    /// <summary>NW diagonal score when the two words are similar but not identical.</summary>
    protected const int NwFuzzyMatch = 2;
    /// <summary>NW diagonal score when the two words do not match.</summary>
    protected const int NwMismatch = -1;
    /// <summary>Score for inserting a gap in the NW alignment.</summary>
    protected const int NwGap = -1;

    /// <summary>Minimum similarity at which two words count as a "fuzzy match".</summary>
    protected const double WordSimilarityThreshold = 0.75;

    /// <summary>Max number of DP cells allowed for full NW; above this, fall back to banded NW.</summary>
    protected const long MaxDpCells = 4_000_000L;

    /// <summary>Minimum bandwidth for banded NW; below this, degrade to sparse mode.</summary>
    protected const int MinBandWidth = 32;

    /// <summary>Search-window size per script word in sparse mode.</summary>
    protected const int SparseSearchWindow = 200;

    /// <summary>
    /// Initializes the base class and passes the logger to the pipeline operator base.
    /// </summary>
    /// <param name="logger">The logger used by derived classes.</param>
    protected TimelineAlignmentOperatorBase(ILogger<TSelf> logger) : base(logger) { }

    // ===== Regexes (GeneratedRegex) =====

    [GeneratedRegex(@"[\p{L}\p{N}]+|[\u4e00-\u9fff]", RegexOptions.Compiled)]
    private static partial Regex TokenPattern();

    [GeneratedRegex(@"[\p{P}\p{S}]", RegexOptions.Compiled)]
    private static partial Regex PunctuationPattern();

    // ================== Word-level NW alignment main entry ==================

    /// <summary>
    /// Word-level NW global alignment. Returns scriptWordToTranscript[i] = j (script word i aligned to transcript word j);
    /// -1 means unaligned. Automatically picks full NW / banded NW / sparse sliding based on size.
    /// </summary>
    protected int[] AlignByWordLevelNw(
        IReadOnlyList<string> scriptWordNorm,
        IReadOnlyList<string> transcriptNorm,
        CancellationToken cancellationToken)
    {
        var sm = scriptWordNorm.Count;
        var n = transcriptNorm.Count;

        var mapping = new int[sm];
        for (var i = 0; i < sm; i++) mapping[i] = -1;
        if (sm == 0 || n == 0) return mapping;

        var fullCells = (long)(sm + 1) * (n + 1);
        if (fullCells <= MaxDpCells)
            return AlignNwFull(scriptWordNorm, transcriptNorm, cancellationToken);

        var bandWidth = (int)(MaxDpCells / (sm + 1) / 2);
        if (bandWidth < MinBandWidth)
        {
            Logger.LogInformation(
                "DP too large ({Cells} cells); falling back to sparse alignment.", fullCells);
            return AlignSparse(scriptWordNorm, transcriptNorm);
        }

        Logger.LogInformation(
            "DP too large ({Cells} cells); using banded NW with width {W}.", fullCells, bandWidth);
        return AlignNwBand(scriptWordNorm, transcriptNorm, bandWidth, cancellationToken);
    }

    /// <summary>Full NW (O(sm*n) space).</summary>
    protected int[] AlignNwFull(
        IReadOnlyList<string> scriptNorm,
        IReadOnlyList<string> transcriptNorm,
        CancellationToken cancellationToken)
    {
        var sm = scriptNorm.Count;
        var n = transcriptNorm.Count;
        var mapping = new int[sm];
        for (var i = 0; i < sm; i++) mapping[i] = -1;

        var dp = new int[sm + 1, n + 1];
        var op = new byte[sm + 1, n + 1];

        for (var i = 1; i <= sm; i++) { dp[i, 0] = i * NwGap; op[i, 0] = 1; }
        for (var j = 1; j <= n; j++) { dp[0, j] = j * NwGap; op[0, j] = 2; }

        for (var i = 1; i <= sm; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var j = 1; j <= n; j++)
            {
                var diag = dp[i - 1, j - 1] + DiagonalScore(scriptNorm[i - 1], transcriptNorm[j - 1]);
                var up = dp[i - 1, j] + NwGap;
                var left = dp[i, j - 1] + NwGap;

                var best = diag;
                byte bestOp = 3;
                if (up > best) { best = up; bestOp = 1; }
                if (left > best) { best = left; bestOp = 2; }

                dp[i, j] = best;
                op[i, j] = bestOp;
            }
        }

        int x = sm, y = n;
        while (x > 0 || y > 0)
        {
            if (x > 0 && y > 0 && op[x, y] == 3)
            {
                if (DiagonalScore(scriptNorm[x - 1], transcriptNorm[y - 1]) != NwMismatch)
                    mapping[x - 1] = y - 1;
                x--; y--;
            }
            else if (x > 0 && op[x, y] == 1) x--;
            else if (y > 0 && op[x, y] == 2) y--;
            else { if (x > 0) x--; else y--; }
        }

        return mapping;
    }

    /// <summary>Banded NW (O(sm*W) space).</summary>
    protected int[] AlignNwBand(
        IReadOnlyList<string> scriptNorm,
        IReadOnlyList<string> transcriptNorm,
        int bandWidth,
        CancellationToken cancellationToken)
    {
        var sm = scriptNorm.Count;
        var n = transcriptNorm.Count;
        var mapping = new int[sm];
        for (var i = 0; i < sm; i++) mapping[i] = -1;

        const int NegInf = int.MinValue / 4;

        var jLoArr = new int[sm + 1];
        var jHiArr = new int[sm + 1];
        for (var i = 0; i <= sm; i++)
        {
            var expectedJ = (long)i * n / sm;
            var jLo = (int)Math.Max(0, expectedJ - bandWidth);
            var jHi = (int)Math.Min(n, expectedJ + bandWidth);
            if (i == 0) jLo = 0;
            if (i == sm) jHi = n;
            jLoArr[i] = jLo;
            jHiArr[i] = jHi;
        }

        var dpRows = new int[sm + 1][];
        var opRows = new byte[sm + 1][];
        for (var i = 0; i <= sm; i++)
        {
            var width = jHiArr[i] - jLoArr[i] + 1;
            dpRows[i] = new int[width];
            opRows[i] = new byte[width];
            Array.Fill(dpRows[i], NegInf);
        }

        dpRows[0][0] = 0;
        for (var j = 1; j <= jHiArr[0]; j++)
        {
            dpRows[0][j - jLoArr[0]] = j * NwGap;
            opRows[0][j - jLoArr[0]] = 2;
        }

        for (var i = 1; i <= sm; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var jLo = jLoArr[i];
            var jHi = jHiArr[i];

            var startJ = jLo;
            if (jLo == 0)
            {
                dpRows[i][0] = i * NwGap;
                opRows[i][0] = 1;
                startJ = 1;
            }

            for (var j = startJ; j <= jHi; j++)
            {
                var k = j - jLo;
                var diag = GetDpBanded(dpRows, jLoArr, i - 1, j - 1, NegInf)
                           + DiagonalScore(scriptNorm[i - 1], transcriptNorm[j - 1]);
                var up = GetDpBanded(dpRows, jLoArr, i - 1, j, NegInf) + NwGap;
                var left = (k > 0 ? dpRows[i][k - 1] : NegInf) + NwGap;

                var best = diag;
                byte bestOp = 3;
                if (up > best) { best = up; bestOp = 1; }
                if (left > best) { best = left; bestOp = 2; }

                dpRows[i][k] = best;
                opRows[i][k] = bestOp;
            }
        }

        int x = sm, y = n;
        while (x > 0 || y > 0)
        {
            if (y < jLoArr[x] || y > jHiArr[x])
            {
                if (x > 0 && y > 0) { x--; y--; }
                else if (x > 0) x--;
                else y--;
                continue;
            }
            var o = opRows[x][y - jLoArr[x]];
            if (x > 0 && y > 0 && o == 3)
            {
                if (DiagonalScore(scriptNorm[x - 1], transcriptNorm[y - 1]) != NwMismatch)
                    mapping[x - 1] = y - 1;
                x--; y--;
            }
            else if (x > 0 && o == 1) x--;
            else if (y > 0 && o == 2) y--;
            else { if (x > 0) x--; else if (y > 0) y--; }
        }

        return mapping;
    }

    /// <summary>Sparse alignment: for each script word, find the best transcript word within the following window.</summary>
    protected int[] AlignSparse(
        IReadOnlyList<string> scriptNorm,
        IReadOnlyList<string> transcriptNorm)
    {
        var sm = scriptNorm.Count;
        var n = transcriptNorm.Count;
        var mapping = new int[sm];
        for (var i = 0; i < sm; i++) mapping[i] = -1;

        var cursor = 0;
        for (var i = 0; i < sm; i++)
        {
            var sw = scriptNorm[i];
            if (string.IsNullOrEmpty(sw)) continue;

            var searchEnd = Math.Min(n, cursor + SparseSearchWindow);
            var bestSim = WordSimilarityThreshold;
            var bestJ = -1;
            for (var j = cursor; j < searchEnd; j++)
            {
                var sim = EditDistanceSimilarity(sw, transcriptNorm[j]);
                if (sim > bestSim) { bestSim = sim; bestJ = j; }
            }
            if (bestJ >= 0)
            {
                mapping[i] = bestJ;
                cursor = bestJ + 1;
            }
        }

        return mapping;
    }

    private static int GetDpBanded(int[][] dpRows, int[] jLoArr, int i, int j, int negInf)
    {
        if (i < 0 || i >= dpRows.Length) return negInf;
        if (j < 0) return negInf;
        var jLo = jLoArr[i];
        var jHi = jLo + dpRows[i].Length - 1;
        if (j < jLo || j > jHi) return negInf;
        return dpRows[i][j - jLo];
    }

    /// <summary>
    /// NW diagonal scoring: exact -> 3, fuzzy (>= threshold) -> 2, mismatch -> -1.
    /// </summary>
    protected static int DiagonalScore(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return NwMismatch;
        if (a == b) return NwExactMatch;

        var sim = EditDistanceSimilarity(a, b);
        return sim >= WordSimilarityThreshold ? NwFuzzyMatch : NwMismatch;
    }

    /// <summary>Normalized edit-distance similarity (0~1).</summary>
    protected static double EditDistanceSimilarity(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0) return 0;
        if (a == b) return 1;

        var previous = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) previous[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            var current = new int[b.Length + 1];
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
            }
            previous = current;
        }

        return 1.0 - (double)previous[^1] / Math.Max(a.Length, b.Length);
    }

    // ================== Sentence-level aggregation ==================

    /// <summary>
    /// Aggregates the "script word -> transcript word" mapping into a
    /// "script sentence -> transcript word closed interval".
    /// Returns alignment[i] = (startWordIndex, endWordIndex); (-1, -1) means the sentence has no match.
    /// </summary>
    protected static (int Start, int End)[] AggregateToSentenceAlignment(
        int[] scriptWordToTranscript,
        IReadOnlyList<int> scriptWordOwner,
        int sentenceCount)
    {
        var alignment = new (int Start, int End)[sentenceCount];
        for (var i = 0; i < sentenceCount; i++) alignment[i] = (-1, -1);

        for (var i = 0; i < scriptWordToTranscript.Length; i++)
        {
            var tj = scriptWordToTranscript[i];
            if (tj < 0) continue;

            var owner = scriptWordOwner[i];
            var cur = alignment[owner];
            if (cur.Start < 0) alignment[owner] = (tj, tj);
            else alignment[owner] = (Math.Min(cur.Start, tj), Math.Max(cur.End, tj));
        }

        return alignment;
    }

    /// <summary>Collects all matched transcript word indices (deduplicated, sorted).</summary>
    protected static SortedSet<int> CollectMatchedTranscriptIndices(int[] scriptWordToTranscript)
    {
        var result = new SortedSet<int>();
        foreach (var idx in scriptWordToTranscript)
            if (idx >= 0) result.Add(idx);
        return result;
    }

    /// <summary>Splits the unmatched transcript word indices into contiguous runs.</summary>
    protected static List<List<int>> SplitUnmatchedSegments(
        int totalWords,
        SortedSet<int> matchedIndices)
    {
        var segments = new List<List<int>>();
        var current = new List<int>();
        for (var i = 0; i < totalWords; i++)
        {
            if (matchedIndices.Contains(i))
            {
                if (current.Count > 0)
                {
                    segments.Add(current);
                    current = new List<int>();
                }
            }
            else
            {
                current.Add(i);
            }
        }
        if (current.Count > 0)
            segments.Add(current);
        return segments;
    }

    // ================== Post-processing ==================

    /// <summary>Unmatched script sentences share the transcript words in the neighboring gaps evenly.</summary>
    protected static void FillUnmatchedFromGaps((int Start, int End)[] alignment, int totalWords)
    {
        var m = alignment.Length;
        if (m == 0 || totalWords == 0) return;

        var i = 0;
        while (i < m)
        {
            if (alignment[i].Start >= 0) { i++; continue; }

            var blockStart = i;
            var blockEnd = i;
            while (blockEnd + 1 < m && alignment[blockEnd + 1].Start < 0)
                blockEnd++;

            var gapStart = 0;
            for (var j = blockStart - 1; j >= 0; j--)
                if (alignment[j].Start >= 0) { gapStart = alignment[j].End + 1; break; }

            var gapEnd = totalWords - 1;
            for (var j = blockEnd + 1; j < m; j++)
                if (alignment[j].Start >= 0) { gapEnd = alignment[j].Start - 1; break; }

            if (gapEnd >= gapStart)
            {
                var blockSize = blockEnd - blockStart + 1;
                var gapSize = gapEnd - gapStart + 1;
                var per = gapSize / blockSize;
                var remainder = gapSize % blockSize;
                var pos = gapStart;

                for (var k = blockStart; k <= blockEnd; k++)
                {
                    var size = per + (k - blockStart < remainder ? 1 : 0);
                    if (size > 0)
                    {
                        alignment[k] = (pos, pos + size - 1);
                        pos += size;
                    }
                }
            }

            i = blockEnd + 1;
        }
    }

    /// <summary>Reattaches leftover transcript words to the adjacent matched sentences.</summary>
    protected static void FillRemainingGaps((int Start, int End)[] alignment, int totalWords)
    {
        if (totalWords == 0 || alignment.Length == 0) return;

        var matched = new List<int>();
        for (var i = 0; i < alignment.Length; i++)
            if (alignment[i].Start >= 0) matched.Add(i);
        if (matched.Count == 0) return;

        if (alignment[matched[0]].Start > 0)
            alignment[matched[0]] = (0, alignment[matched[0]].End);

        for (var t = 0; t < matched.Count - 1; t++)
        {
            var li = matched[t];
            var ri = matched[t + 1];

            var gapStart = alignment[li].End + 1;
            var gapEnd = alignment[ri].Start - 1;
            if (gapEnd < gapStart) continue;

            var gapSize = gapEnd - gapStart + 1;
            var toLeft = gapSize / 2;
            var toRight = gapSize - toLeft;

            if (toLeft > 0)
                alignment[li] = (alignment[li].Start, alignment[li].End + toLeft);
            if (toRight > 0)
                alignment[ri] = (alignment[ri].Start - toRight, alignment[ri].End);
        }

        var last = matched[^1];
        if (alignment[last].End < totalWords - 1)
            alignment[last] = (alignment[last].Start, totalWords - 1);
    }

    /// <summary>
    /// Ensures the output sentence timeline is strictly monotonically increasing:
    ///   • overlapping the previous sentence -> clip from prevEnd;
    ///   • fully contained within it -> SkipRender directly;
    ///   • zero/negative duration -> SkipRender.
    /// </summary>
    protected static void EnforceMonotonicTime(IList<Sentence> sentences)
    {
        var prevEnd = double.NegativeInfinity;
        foreach (var s in sentences)
        {
            if (s.SkipRender) continue;

            if (s.End <= s.Start)
            {
                s.SkipRender = true;
                continue;
            }

            if (s.Start < prevEnd)
            {
                if (s.End <= prevEnd)
                {
                    s.SkipRender = true;
                    continue;
                }
                s.Start = prevEnd;
            }

            prevEnd = s.End;
        }
    }

    // ================== Word processing ==================

    /// <summary>Lowercase + strip punctuation, used for NW comparison.</summary>
    protected static string NormalizeWord(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var lowered = text.ToLowerInvariant();
        return PunctuationPattern().Replace(lowered, string.Empty).Trim();
    }

    /// <summary>Extracts the token sequence via TokenPattern.</summary>
    protected static IEnumerable<string> ExtractTokens(string text)
        => TokenPattern().Matches(text).Select(m => m.Value);

    /// <summary>Gets sentence text: prefers Text, falls back to joining Words.</summary>
    protected static string GetSentenceText(Sentence sentence)
        => !string.IsNullOrWhiteSpace(sentence.Text)
            ? sentence.Text!
            : sentence.Words is { Count: > 0 }
                ? string.Join(" ", sentence.Words.Select(w => w.Text))
                : string.Empty;

    /// <summary>Splits raw text into a list of normalized tokens.</summary>
    protected static List<(string Original, string Normalized)> BuildScriptTokens(string text)
    {
        var rawTokens = TokenPattern().Matches(text).Select(m => m.Value).ToList();
        var result = new List<(string, string)>();
        foreach (var raw in rawTokens)
        {
            var normalized = NormalizeWord(raw);
            if (string.IsNullOrWhiteSpace(normalized))
                continue;
            result.Add((raw, normalized));
        }
        return result;
    }
}
