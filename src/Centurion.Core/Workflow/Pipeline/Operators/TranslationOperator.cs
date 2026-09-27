using Centurion.Abstractions.Pipeline;
using Centurion.Abstractions.Strategy;
using Centurion.Models;
using Centurion.Models.Workflow;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Workflow.Pipeline.Operators;

/// <summary>
/// 翻译算子：把当前工作集句子翻译到目标语言（策略内部按批并行调用 LLM），
/// 译文写回各句 TranslatedText，时间轴与词级明细保持不变。
/// 由 TranslateCommand 与 pipeline graph 命令共享装配。
/// </summary>
public sealed class TranslationOperator(
    ITranslationStrategy strategy,
    TranslationOptions options,
    ILogger<TranslationOperator> logger) : PipelineOperatorBase<TranslationOperator>(logger)
{
    private readonly ITranslationStrategy _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
    private readonly TranslationOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    /// <summary>算子在管道中的显示名称。</summary>
    public override string Name => "Translation";

    /// <summary>执行翻译并更新工作流状态（TranslatedSentences / CurrentSentences / IsTranslated）。</summary>
    public override async Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken)
    {
        var sentences = context.State.CurrentSentences;
        if (sentences.Count == 0)
        {
            LogInfo("No sentences to translate; skipping translation.");
            context.State.IsTranslated = true;
            return;
        }

        OnProgress(10, $"Translating {sentences.Count} sentences to {_options.TargetLanguage}");
        LogInfo($"Using translation strategy: {_strategy.StrategyName} (batch size {_options.BatchSize}, concurrency {_options.MaxConcurrency})");

        await _strategy.TranslateAsync(sentences, _options, cancellationToken);

        context.State.TranslatedSentences = sentences;
        context.State.CurrentSentences = sentences;
        context.State.IsTranslated = true;
        context.State.TranslationQa = BuildTranslationQa(sentences, _options);
        OnProgress(100, "Translation completed");
        LogInfo($"Translation completed ({_options.TargetLanguage}).");
    }

    /// <summary>计算翻译 QA：术语命中率 + 译文/原文长度偏差（纯文本统计，不额外调用 LLM）。</summary>
    internal static TranslationQa BuildTranslationQa(List<Sentence> sentences, TranslationOptions options)
    {
        var qa = new TranslationQa { TranslatedCount = sentences.Count(s => !string.IsNullOrWhiteSpace(s.TranslatedText)) };

        if (options.Glossary.Count > 0)
        {
            foreach (var sentence in sentences)
            {
                var source = sentence.Text ?? string.Empty;
                var translated = sentence.TranslatedText ?? string.Empty;
                foreach (var (sourceTerm, targetTerm) in options.Glossary)
                {
                    if (!source.Contains(sourceTerm, StringComparison.OrdinalIgnoreCase))
                        continue;
                    qa.GlossaryExpected++;
                    if (translated.Contains(targetTerm, StringComparison.Ordinal))
                        qa.GlossaryHits++;
                }
            }
            qa.GlossaryHitRate = qa.GlossaryExpected > 0
                ? Math.Round(qa.GlossaryHits / (double)qa.GlossaryExpected, 3)
                : 1;
        }

        var ratios = sentences
            .Where(s => !string.IsNullOrWhiteSpace(s.TranslatedText) && !string.IsNullOrWhiteSpace(s.Text))
            .Select(s => NonBlankChars(s.TranslatedText!) / (double)Math.Max(1, NonBlankChars(s.Text!)))
            .ToList();
        if (ratios.Count > 0)
        {
            qa.MeanLengthRatio = Math.Round(ratios.Average(), 3);
            qa.LengthDeviation = Math.Round(ratios.Average(r => Math.Abs(r - 1.0)), 3);
        }

        return qa;
    }

    private static int NonBlankChars(string text) => text.Count(ch => !char.IsWhiteSpace(ch));
}
