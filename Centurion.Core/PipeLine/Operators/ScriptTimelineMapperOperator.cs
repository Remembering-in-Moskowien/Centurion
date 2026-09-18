using Centurion.Core.Pipeline;
using Centurion.Models;
using Centurion.Models.Workflow;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Pipeline.Operators;

/// <summary>
/// 脚本句 ↔ 转录时间轴对齐。
///
/// 分段策略（唯一准则）：
///   • 输出分段严格采用脚本句（<c>CurrentSentences</c>）；一句脚本 → 一句输出；
///   • 不做聚合（合并相邻脚本句），不做分句（拆分单个脚本句）；
///   • 转录仅作为时间戳与纠错文本的来源，其断句不参与最终分段。
///
/// 对齐流程（四级）：
///   1. 词级 NW 全局对齐（精确匹配优先作为"锚点"）；
///   2. 未命中的脚本句在相邻命中之间的空隙内均分转录词；
///   3. 仍未被任何句覆盖的"边界残余词"按最近距离补回相邻句；
///   4. 输出时间单调性二次校验。
///
/// 规模策略由基类 <see cref="TimelineAlignmentOperatorBase{TSelf}"/> 提供。
/// </summary>
public sealed class ScriptTimelineMapperOperator : TimelineAlignmentOperatorBase<ScriptTimelineMapperOperator>
{
    private readonly ILogger<ScriptTimelineMapperOperator> _logger;

    public ScriptTimelineMapperOperator(ILogger<ScriptTimelineMapperOperator> logger) : base(logger)
    {
        _logger = logger;
    }

    public override string Name => "Script Timeline Mapping";

    public override Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken)
    {
        // ====== 脚本句：输出的唯一分段依据 ======
        var scriptSentences = context.State.CurrentSentences;
        if (scriptSentences.Count == 0)
        {
            const string message = "No current sentences are available for script timeline mapping.";
            context.State.Errors.Add(message);
            _logger.LogError(message);
            throw new InvalidOperationException(message);
        }

        // ====== 转录：把 Words 打平成词流 ======
        var transcriptWords = new List<Word>();
        foreach (var s in context.State.TranscribeSentences)
            if (s.Words is { Count: > 0 })
                transcriptWords.AddRange(s.Words);

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
            _logger.LogWarning("Script mapping skipped because transcript words are empty.");
            return Task.CompletedTask;
        }

        // ==== 阶段 1：词级 NW 全局对齐 ====
        var alignment = AlignSentencesToTranscript(
            scriptSentences, transcriptWords, cancellationToken);

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
                foreach (var (original, _) in BuildScriptTokens(GetSentenceText(sentence)))
                {
                    sentence.Words.Add(new Word
                    {
                        Text = original,
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
            _logger.LogInformation(
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
            _logger.LogWarning(warning);
        }
        else
        {
            _logger.LogInformation("Script mapping coverage: {Coverage:P1}.", context.State.MapperCoverage);
        }

        OnProgress(100, $"Mapped script with {context.State.MapperCoverage:P1} coverage.");
        return Task.CompletedTask;
    }

    /// <summary>把句子集与转录词做词级 NW 对齐，返回句子级 (Start, End) 聚合。</summary>
    private (int Start, int End)[] AlignSentencesToTranscript(
        IList<Sentence> sentences,
        IList<Word> transcriptWords,
        CancellationToken cancellationToken)
    {
        var m = sentences.Count;
        var n = transcriptWords.Count;

        var alignment = new (int Start, int End)[m];
        for (var i = 0; i < m; i++) alignment[i] = (-1, -1);
        if (n == 0 || m == 0) return alignment;

        // 展平脚本词流
        var scriptWordNorm = new List<string>();
        var scriptWordOwner = new List<int>();
        for (var i = 0; i < m; i++)
        {
            foreach (var token in ExtractTokens(GetSentenceText(sentences[i])))
            {
                var norm = NormalizeWord(token);
                if (string.IsNullOrEmpty(norm)) continue;
                scriptWordNorm.Add(norm);
                scriptWordOwner.Add(i);
            }
        }

        if (scriptWordNorm.Count == 0) return alignment;

        var transcriptNorm = new string[n];
        for (var j = 0; j < n; j++)
            transcriptNorm[j] = NormalizeWord(transcriptWords[j].Text);

        var scriptToTranscript = AlignByWordLevelNw(scriptWordNorm, transcriptNorm, cancellationToken);
        return AggregateToSentenceAlignment(scriptToTranscript, scriptWordOwner, m);
    }

    private static void ResetSentence(Sentence sentence)
    {
        sentence.Words.Clear();
        sentence.Start = 0;
        sentence.End = 0;
        sentence.SkipRender = false;
    }
}
