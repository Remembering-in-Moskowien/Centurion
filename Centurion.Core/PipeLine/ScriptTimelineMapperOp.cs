using System.Text;
using System.Text.RegularExpressions;
using Centurion.Core.Abstractions;
using Centurion.Core.Models;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.PipeLine;

/// <summary>
/// 脚本句 <-> 转录时间轴对齐。
///
/// 分段策略（唯一准则）：
///   • 输出分段严格采用脚本句（<c>CurrentSentences</c>）；一句脚本 → 一句输出；
///   • 不做聚合（合并相邻脚本句），不做分句（拆分单个脚本句）；
///   • 转录仅作为时间戳与纠错文本的来源，其断句不参与最终分段。
///
/// 对齐流程（四级）：
///   1. 词级 NW 全局对齐（精确匹配优先作为“锚点”）；
///   2. 未命中的脚本句在相邻命中之间的空隙内均分转录词；
///   3. 仍未被任何句覆盖的“边界残余词”按最近距离补回相邻句；
///   4. 输出时间单调性二次校验。
///
/// 规模策略：完整 NW → 带状 NW → 稀疏滑动，保证大输入不 OOM。
/// </summary>
public sealed class ScriptTimelineMapperOp(ILogger<ScriptTimelineMapperOp> logger) : PipelineOperatorBase(logger)
{
    private static readonly Regex TokenPattern =
        new(@"[\p{L}\p{N}]+|[\u4e00-\u9fff]", RegexOptions.Compiled);

    private static readonly Regex PunctuationPattern =
        new(@"[\p{P}\p{S}]", RegexOptions.Compiled);

    // ===== NW 打分 =====
    private const int NwExactMatch = 3;   // 精确匹配（锚点）
    private const int NwFuzzyMatch = 2;   // 模糊匹配
    private const int NwMismatch = -1;    // 不匹配
    private const int NwGap = -1;         // 间隙

    /// <summary>判定两词“模糊匹配”的最低相似度。</summary>
    private const double WordSimilarityThreshold = 0.75;

    /// <summary>完整 NW 允许的最大 DP 单元数；超出则改用带状 NW。</summary>
    private const long MaxDpCells = 4_000_000L;

    /// <summary>带状 NW 的最小带宽；再小则退化为稀疏模式。</summary>
    private const int MinBandWidth = 32;

    /// <summary>稀疏模式下每个脚本词的搜索窗口大小。</summary>
    private const int SparseSearchWindow = 200;

    public override string Name => "Script Timeline Mapping";

    public override Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken)
    {
        // ====== 脚本句：输出的唯一分段依据 ======
        var scriptSentences = context.State.CurrentSentences;
        if (scriptSentences.Count == 0)
        {
            const string message = "No current sentences are available for script timeline mapping.";
            context.State.Errors.Add(message);
            logger.LogError(message);
            throw new InvalidOperationException(message);
        }

        // ====== 转录：把 Words 打平成词流（转录常只有 1 段） ======
        var transcriptWords = new List<Word>();
        foreach (var s in context.State.TranscribeSentences)
        {
            if (s.Words is { Count: > 0 })
                transcriptWords.AddRange(s.Words);
        }

        if (transcriptWords.Count == 0)
        {
            context.State.MapperCoverage = 0;
            foreach (var sentence in scriptSentences)
            {
                ResetSentence(sentence);
                sentence.SkipRender = true;
            }

            context.State.CoarseSentences = scriptSentences;
            context.State.CurrentSentences = scriptSentences;
            logger.LogWarning("Script mapping skipped because transcript words are empty.");
            return Task.CompletedTask;
        }

        // ==== 阶段 1：词级 NW 全局对齐 ====
        var alignment = AlignByWordLevelNw(scriptSentences, transcriptWords, cancellationToken);

        // 覆盖率用“NW 阶段真实命中”统计（不含填充）
        var trueMatchedCount = 0;
        for (var i = 0; i < alignment.Length; i++)
            if (alignment[i].Start >= 0) trueMatchedCount++;

        // ==== 阶段 2：未命中脚本句在邻居空隙内均分转录词 ====
        FillUnmatchedFromGaps(alignment, transcriptWords.Count);

        // ==== 阶段 3：把仍未被覆盖的转录词补回相邻已命中句 ====
        FillRemainingGaps(alignment, transcriptWords.Count);

        // ==== 阶段 4：以脚本句为骨架输出 ====
        for (var i = 0; i < scriptSentences.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sentence = scriptSentences[i];
            ResetSentence(sentence);

            var range = alignment[i];
            if (range.Start >= 0 && range.End >= range.Start)
            {
                for (var k = range.Start; k <= range.End && k < transcriptWords.Count; k++)
                {
                    var word = transcriptWords[k];
                    sentence.Words.Add(new Word
                    {
                        Text = word.Text,
                        Start = word.Start,
                        End = word.End,
                        Speaker = string.IsNullOrEmpty(word.Speaker) ? "UNKNOWN" : word.Speaker,
                        Status = MappingStatus.Matched
                    });
                }
            }
            else
            {
                foreach (var token in BuildScriptTokens(sentence.Text ?? string.Empty, context.Config))
                {
                    sentence.Words.Add(new Word
                    {
                        Text = token.Original,
                        Start = 0,
                        End = 0,
                        Speaker = "UNKNOWN",
                        Status = MappingStatus.ScriptMissing
                    });
                }
            }

            var timedWords = sentence.Words.Where(w => w.End > w.Start).ToList();
            if (timedWords.Count > 0)
            {
                sentence.Start = timedWords.Min(w => w.Start);
                sentence.End = timedWords.Max(w => w.End);
            }

            if (sentence.End <= sentence.Start)
                sentence.SkipRender = true;
        }

        // ==== 阶段 5：时间单调性二次校验 ====
        EnforceMonotonicTime(scriptSentences);

        for (var i = 0; i < scriptSentences.Count; i++)
        {
            var sentence = scriptSentences[i];
            logger.LogInformation(
                "Mapped script sentence {Index}/{Total}: Start={Start:F0}ms End={End:F0}ms Matched={Matched} SkipRender={SkipRender}",
                i + 1, scriptSentences.Count, sentence.Start, sentence.End,
                alignment[i].Start >= 0, sentence.SkipRender);
        }

        context.State.MapperCoverage = (double)trueMatchedCount / scriptSentences.Count;
        context.State.CoarseSentences = scriptSentences;
        context.State.CurrentSentences = scriptSentences;

        if (context.State.MapperCoverage < context.Config.CoverageThreshold)
        {
            var warning =
                $"Script mapping coverage {context.State.MapperCoverage:P1} is below threshold {context.Config.CoverageThreshold:P1}.";
            context.State.Warnings.Add(warning);
            logger.LogWarning(warning);
        }
        else
        {
            logger.LogInformation("Script mapping coverage: {Coverage:P1}.", context.State.MapperCoverage);
        }

        OnProgress(100, $"Mapped script with {context.State.MapperCoverage:P1} coverage.");
        return Task.CompletedTask;
    }

    private static void ResetSentence(Sentence sentence)
    {
        sentence.Words.Clear();
        sentence.Extensions.Clear();
        sentence.Start = 0;
        sentence.End = 0;
        sentence.SkipRender = false;
    }

    // ================== 阶段 1：词级 NW ==================

    /// <summary>
    /// 词级 NW 全局对齐。
    /// 返回 alignment[i] = (startWordIndex, endWordIndex)，表示脚本句 i 使用的转录词闭区间；
    /// (-1, -1) 表示 NW 未给该句任何直接对齐的转录词。
    /// </summary>
    private (int Start, int End)[] AlignByWordLevelNw(
        IList<Sentence> scripts,
        IList<Word> transcriptWords,
        CancellationToken cancellationToken)
    {
        var m = scripts.Count;
        var n = transcriptWords.Count;

        var alignment = new (int Start, int End)[m];
        for (var i = 0; i < m; i++) alignment[i] = (-1, -1);
        if (n == 0 || m == 0) return alignment;

        // 展平脚本词流
        var scriptWordNorm = new List<string>();
        var scriptWordOwner = new List<int>();
        for (var i = 0; i < m; i++)
        {
            foreach (var token in ExtractTokens(scripts[i].Text ?? string.Empty))
            {
                scriptWordNorm.Add(NormalizeWord(token));
                scriptWordOwner.Add(i);
            }
        }
        var sm = scriptWordNorm.Count;
        if (sm == 0) return alignment;

        // 转录词归一化
        var transcriptNorm = new string[n];
        for (var j = 0; j < n; j++)
            transcriptNorm[j] = NormalizeWord(transcriptWords[j].Text);

        // ---- 三级规模策略 ----
        var fullCells = (long)(sm + 1) * (n + 1);
        if (fullCells <= MaxDpCells)
        {
            return AlignNw(scriptWordNorm, scriptWordOwner, transcriptNorm, m, n, cancellationToken);
        }

        // 带宽使总单元格 ~ MaxDpCells：每行约 (2W+1) 宽
        var bandWidth = (int)(MaxDpCells / (sm + 1) / 2);
        if (bandWidth < MinBandWidth)
        {
            logger.LogInformation(
                "DP too large ({Cells} cells); falling back to sparse alignment.", fullCells);
            return AlignSparse(scriptWordNorm, scriptWordOwner, transcriptNorm, m, n);
        }

        logger.LogInformation(
            "DP too large ({Cells} cells); using banded NW with width {W}.", fullCells, bandWidth);
        return AlignNwBand(scriptWordNorm, scriptWordOwner, transcriptNorm, m, n, bandWidth, cancellationToken);
    }

    /// <summary>完整 NW（O(sm·n) 空间）。</summary>
    private static (int Start, int End)[] AlignNw(
        List<string> scriptWordNorm,
        List<int> scriptWordOwner,
        string[] transcriptNorm,
        int m,
        int n,
        CancellationToken cancellationToken)
    {
        var sm = scriptWordNorm.Count;
        var dp = new int[sm + 1, n + 1];
        var op = new byte[sm + 1, n + 1];

        for (var i = 1; i <= sm; i++) { dp[i, 0] = i * NwGap; op[i, 0] = 1; }
        for (var j = 1; j <= n; j++) { dp[0, j] = j * NwGap; op[0, j] = 2; }

        for (var i = 1; i <= sm; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var j = 1; j <= n; j++)
            {
                var diagScore = dp[i - 1, j - 1] + DiagonalScore(
                    scriptWordNorm[i - 1], transcriptNorm[j - 1]);
                var upScore = dp[i - 1, j] + NwGap;
                var leftScore = dp[i, j - 1] + NwGap;

                var best = diagScore;
                byte bestOp = 3;
                if (upScore > best) { best = upScore; bestOp = 1; }
                if (leftScore > best) { best = leftScore; bestOp = 2; }

                dp[i, j] = best;
                op[i, j] = bestOp;
            }
        }

        return Backtrack(scriptWordNorm, scriptWordOwner, transcriptNorm, op, sm, n, m);
    }

    /// <summary>带状 NW（O(sm·W) 空间）。</summary>
    private static (int Start, int End)[] AlignNwBand(
        List<string> scriptWordNorm,
        List<int> scriptWordOwner,
        string[] transcriptNorm,
        int m,
        int n,
        int bandWidth,
        CancellationToken cancellationToken)
    {
        var sm = scriptWordNorm.Count;
        const int NegInf = int.MinValue / 4;

        // 每行的列范围（以“按比例对角线”为中心）
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
                           + DiagonalScore(scriptWordNorm[i - 1], transcriptNorm[j - 1]);
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

        // 回溯
        var scriptToTranscript = new int[sm];
        for (var i = 0; i < sm; i++) scriptToTranscript[i] = -1;

        int x = sm, y = n;
        while (x > 0 || y > 0)
        {
            if (y < jLoArr[x] || y > jHiArr[x])
            {
                // 带外（极端情形）——朝原点退一步
                if (x > 0 && y > 0) { x--; y--; }
                else if (x > 0) x--;
                else y--;
                continue;
            }
            var o = opRows[x][y - jLoArr[x]];
            if (x > 0 && y > 0 && o == 3)
            {
                var sim = WordSimilarity(scriptWordNorm[x - 1], transcriptNorm[y - 1]);
                if (sim > 0) scriptToTranscript[x - 1] = y - 1;
                x--; y--;
            }
            else if (x > 0 && o == 1) x--;
            else if (y > 0 && o == 2) y--;
            else { if (x > 0) x--; else if (y > 0) y--; }
        }

        return BuildAlignmentFromScriptMap(scriptToTranscript, scriptWordOwner, m);
    }

    /// <summary>稀疏对齐：每个脚本词在后续窗口中找最佳转录词。</summary>
    private static (int Start, int End)[] AlignSparse(
        List<string> scriptWordNorm,
        List<int> scriptWordOwner,
        string[] transcriptNorm,
        int m,
        int n)
    {
        var sm = scriptWordNorm.Count;
        var scriptToTranscript = new int[sm];
        for (var i = 0; i < sm; i++) scriptToTranscript[i] = -1;

        var cursor = 0;
        for (var i = 0; i < sm; i++)
        {
            var sw = scriptWordNorm[i];
            if (string.IsNullOrEmpty(sw)) continue;

            var searchEnd = Math.Min(n, cursor + SparseSearchWindow);
            var bestSim = WordSimilarityThreshold;
            var bestJ = -1;
            for (var j = cursor; j < searchEnd; j++)
            {
                var sim = WordSimilarity(sw, transcriptNorm[j]);
                if (sim > bestSim) { bestSim = sim; bestJ = j; }
            }
            if (bestJ >= 0)
            {
                scriptToTranscript[i] = bestJ;
                cursor = bestJ + 1;
            }
        }

        return BuildAlignmentFromScriptMap(scriptToTranscript, scriptWordOwner, m);
    }

    /// <summary>
    /// NW 对角线打分：
    ///   精确匹配 → NwExactMatch（作为“锚点”，优先选中）
    ///   模糊匹配 → NwFuzzyMatch
    ///   不匹配   → NwMismatch
    /// </summary>
    private static int DiagonalScore(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return NwMismatch;
        if (a == b) return NwExactMatch;

        var sim = EditDistanceSimilarity(a, b);
        return sim >= WordSimilarityThreshold ? NwFuzzyMatch : NwMismatch;
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

    private static (int Start, int End)[] Backtrack(
        List<string> scriptWordNorm,
        List<int> scriptWordOwner,
        string[] transcriptNorm,
        byte[,] op,
        int sm,
        int n,
        int m)
    {
        var scriptToTranscript = new int[sm];
        for (var i = 0; i < sm; i++) scriptToTranscript[i] = -1;

        int x = sm, y = n;
        while (x > 0 || y > 0)
        {
            if (x > 0 && y > 0 && op[x, y] == 3)
            {
                var sim = WordSimilarity(scriptWordNorm[x - 1], transcriptNorm[y - 1]);
                if (sim > 0) scriptToTranscript[x - 1] = y - 1;
                x--; y--;
            }
            else if (x > 0 && op[x, y] == 1) x--;
            else if (y > 0 && op[x, y] == 2) y--;
            else { if (x > 0) x--; else if (y > 0) y--; }
        }

        return BuildAlignmentFromScriptMap(scriptToTranscript, scriptWordOwner, m);
    }

    /// <summary>把“脚本词 → 转录词”映射聚合为“脚本句 → 转录词闭区间”。</summary>
    private static (int Start, int End)[] BuildAlignmentFromScriptMap(
        int[] scriptToTranscript,
        List<int> scriptWordOwner,
        int m)
    {
        var alignment = new (int Start, int End)[m];
        for (var i = 0; i < m; i++) alignment[i] = (-1, -1);

        for (var i = 0; i < scriptToTranscript.Length; i++)
        {
            var tj = scriptToTranscript[i];
            if (tj < 0) continue;

            var owner = scriptWordOwner[i];
            var cur = alignment[owner];
            if (cur.Start < 0) alignment[owner] = (tj, tj);
            else alignment[owner] = (Math.Min(cur.Start, tj), Math.Max(cur.End, tj));
        }

        return alignment;
    }

    // ================== 阶段 2：未命中句均分空隙 ==================

    /// <summary>
    /// 未命中的脚本句，在相邻命中之间的空隙内按句均分转录词；
    /// 首尾缺失时退化为从头/到末尾。
    /// </summary>
    private static void FillUnmatchedFromGaps((int Start, int End)[] alignment, int totalWords)
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

    // ================== 阶段 3：边界残余词回收 ==================

    /// <summary>
    /// 把仍未被任何句覆盖的转录词，按最近距离补回相邻已命中句。
    ///
    /// 处理“两个已命中句之间没有未命中句、但夹着 1~N 个未覆盖词”的场景
    /// （例如上一句尾部或下一句头部的词被 NW 当作 gap 丢弃）。
    /// 做法：两个相邻已命中范围之间对半平摊；首/尾残余直接并入最近句。
    /// </summary>
    private static void FillRemainingGaps((int Start, int End)[] alignment, int totalWords)
    {
        if (totalWords == 0 || alignment.Length == 0) return;

        var matched = new List<int>();
        for (var i = 0; i < alignment.Length; i++)
            if (alignment[i].Start >= 0) matched.Add(i);
        if (matched.Count == 0) return;

        // 头部：第一个已命中句之前的所有词
        if (alignment[matched[0]].Start > 0)
            alignment[matched[0]] = (0, alignment[matched[0]].End);

        // 中间：相邻已命中范围之间的空隙对半平摊（先快照边界，再更新）
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

        // 尾部：最后一个已命中句之后的所有词
        var last = matched[^1];
        if (alignment[last].End < totalWords - 1)
            alignment[last] = (alignment[last].Start, totalWords - 1);
    }

    // ================== 阶段 5：时间单调性二次校验 ==================

    /// <summary>
    /// 保证输出句的时间轴严格单调递增：
    ///   • 与前句重叠的 → 从 prevEnd 起裁剪；
    ///   • 被完全包含的 → 直接 SkipRender；
    ///   • 零/负时长的 → SkipRender。
    /// </summary>
    private static void EnforceMonotonicTime(IList<Sentence> sentences)
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

    private static IEnumerable<string> ExtractTokens(string text)
        => TokenPattern.Matches(text).Select(m => m.Value);

    private static string NormalizeWord(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var lowered = text.ToLowerInvariant();
        return PunctuationPattern.Replace(lowered, string.Empty).Trim();
    }

    /// <summary>
    /// 两词相似度（0~1）。命中返回 ≥ 阈值的值，否则返回 0。
    /// 短词不再一刀切：精确匹配由 <c>a == b</c> 处理，模糊匹配仍由 0.75 阈值把关。
    /// </summary>
    private static double WordSimilarity(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0;
        if (a == b) return 1.0;

        var sim = EditDistanceSimilarity(a, b);
        return sim >= WordSimilarityThreshold ? sim : 0;
    }

    private static double EditDistanceSimilarity(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0) return 0;
        if (a == b) return 1;

        var previous = Enumerable.Range(0, b.Length + 1).ToArray();
        for (var i = 1; i <= a.Length; i++)
        {
            var current = new int[b.Length + 1];
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            }
            previous = current;
        }

        return 1.0 - (double)previous[^1] / Math.Max(a.Length, b.Length);
    }

    private static string GetSentenceText(Sentence sentence)
        => !string.IsNullOrWhiteSpace(sentence.Text)
            ? sentence.Text!
            : sentence.Words is { Count: > 0 }
                ? string.Join(" ", sentence.Words.Select(w => w.Text))
                : string.Empty;

    private static string Normalize(string text, WorkflowConfig config)
    {
        var normalized = TextPreprocessingOp.CleanText(text, config);
        return PunctuationPattern.Replace(normalized, "").Trim();
    }

    private sealed record ScriptToken(string Original, string Normalized);

    private static List<ScriptToken> BuildScriptTokens(string text, WorkflowConfig config)
    {
        var rawTokens = TokenPattern.Matches(text).Select(m => m.Value).ToList();
        var result = new List<ScriptToken>();
        foreach (var raw in rawTokens)
        {
            var normalized = Normalize(raw, config);
            if (string.IsNullOrWhiteSpace(normalized))
                continue;
            result.Add(new ScriptToken(raw, normalized));
        }
        return result;
    }
}