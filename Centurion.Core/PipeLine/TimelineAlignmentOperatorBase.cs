using Centurion.Abstractions.Pipeline;
using System.Text.RegularExpressions;
using Centurion.Models;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Pipeline;

/// <summary>
/// 时间轴对齐算子的共享基类。
///
/// 提供词级 NW 全局对齐、句子级聚合、空隙填充、时间单调性校验等工具，
/// 供 ScriptTimelineMapperOperator（台本打轴）与
/// 时间轴映射与校正算子共同使用。
///
/// NW 打分（统一约定）：
///   精确匹配 → 3
///   模糊匹配（≥ 0.75）→ 2
///   不匹配 → -1
///   间隙 → -1
///
/// 规模策略：完整 NW → 带状 NW → 稀疏滑动，保证大输入不 OOM。
/// </summary>
public abstract partial class TimelineAlignmentOperatorBase<TSelf> : PipelineOperatorBase<TSelf>
    where TSelf : TimelineAlignmentOperatorBase<TSelf>
{
    // ===== NW 打分 =====
    /// <summary>两词完全相同时的 NW 对角线得分。</summary>
    protected const int NwExactMatch = 3;
    /// <summary>两词相似度达到阈值但不完全相同时的 NW 对角线得分。</summary>
    protected const int NwFuzzyMatch = 2;
    /// <summary>两词不匹配时的 NW 对角线得分。</summary>
    protected const int NwMismatch = -1;
    /// <summary>在 NW 对齐中插入空隙（gap）时的得分。</summary>
    protected const int NwGap = -1;

    /// <summary>判定两词"模糊匹配"的最低相似度。</summary>
    protected const double WordSimilarityThreshold = 0.75;

    /// <summary>完整 NW 允许的最大 DP 单元数；超出则改用带状 NW。</summary>
    protected const long MaxDpCells = 4_000_000L;

    /// <summary>带状 NW 的最小带宽；再小则退化为稀疏模式。</summary>
    protected const int MinBandWidth = 32;

    /// <summary>稀疏模式下每个脚本词的搜索窗口大小。</summary>
    protected const int SparseSearchWindow = 200;

    /// <summary>
    /// 初始化基类，并将日志器传给管线算子基类。
    /// </summary>
    /// <param name="logger">派生类使用的日志器。</param>
    protected TimelineAlignmentOperatorBase(ILogger<TSelf> logger) : base(logger) { }

    // ===== 正则（GeneratedRegex）=====

    [GeneratedRegex(@"[\p{L}\p{N}]+|[\u4e00-\u9fff]", RegexOptions.Compiled)]
    private static partial Regex TokenPattern();

    [GeneratedRegex(@"[\p{P}\p{S}]", RegexOptions.Compiled)]
    private static partial Regex PunctuationPattern();

    // ================== 词级 NW 对齐主入口 ==================

    /// <summary>
    /// 词级 NW 全局对齐。返回 scriptWordToTranscript[i] = j（脚本词 i 对齐到转录词 j），-1 表示未对齐。
    /// 根据规模自动选择完整 NW / 带状 NW / 稀疏滑动。
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

    /// <summary>完整 NW（O(sm·n) 空间）。</summary>
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

    /// <summary>带状 NW（O(sm·W) 空间）。</summary>
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

    /// <summary>稀疏对齐：每个脚本词在后续窗口中找最佳转录词。</summary>
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
    /// NW 对角线打分：精确 → 3，模糊（≥阈值）→ 2，不匹配 → -1。
    /// </summary>
    protected static int DiagonalScore(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return NwMismatch;
        if (a == b) return NwExactMatch;

        var sim = EditDistanceSimilarity(a, b);
        return sim >= WordSimilarityThreshold ? NwFuzzyMatch : NwMismatch;
    }

    /// <summary>归一化编辑距离相似度（0~1）。</summary>
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

    // ================== 句子级聚合 ==================

    /// <summary>
    /// 把"脚本词 → 转录词"映射聚合为"脚本句 → 转录词闭区间"。
    /// 返回 alignment[i] = (startWordIndex, endWordIndex)，(-1, -1) 表示该句未匹配。
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

    /// <summary>收集所有被匹配的转录词索引（去重、排序）。</summary>
    protected static SortedSet<int> CollectMatchedTranscriptIndices(int[] scriptWordToTranscript)
    {
        var result = new SortedSet<int>();
        foreach (var idx in scriptWordToTranscript)
            if (idx >= 0) result.Add(idx);
        return result;
    }

    /// <summary>按连续段切分未被匹配的转录词索引。</summary>
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

    // ================== 后处理 ==================

    /// <summary>未命中的脚本句在邻居空隙内均分转录词。</summary>
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

    /// <summary>把残余转录词补回相邻已命中句。</summary>
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
    /// 保证输出句的时间轴严格单调递增：
    ///   • 与前句重叠的 → 从 prevEnd 起裁剪；
    ///   • 被完全包含的 → 直接 SkipRender；
    ///   • 零/负时长的 → SkipRender。
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

    // ================== 词处理 ==================

    /// <summary>小写 + 去标点，用于 NW 比对。</summary>
    protected static string NormalizeWord(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var lowered = text.ToLowerInvariant();
        return PunctuationPattern().Replace(lowered, string.Empty).Trim();
    }

    /// <summary>按 TokenPattern 提取 token 序列。</summary>
    protected static IEnumerable<string> ExtractTokens(string text)
        => TokenPattern().Matches(text).Select(m => m.Value);

    /// <summary>取句子文本：优先 Text，回退从 Words 拼接。</summary>
    protected static string GetSentenceText(Sentence sentence)
        => !string.IsNullOrWhiteSpace(sentence.Text)
            ? sentence.Text!
            : sentence.Words is { Count: > 0 }
                ? string.Join(" ", sentence.Words.Select(w => w.Text))
                : string.Empty;

    /// <summary>把原始文本切分为归一化后的 token 列表。</summary>
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
