using Centurion.Abstractions.Pipeline;
using Centurion.Abstractions.Strategy;
using Centurion.Models;
using Centurion.Models.Workflow;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Workflow.Pipeline.Operators;

/// <summary>
/// Translation operator: translates the current working set of sentences into the target language (the strategy internally calls the LLM in parallel batches),
/// writes the translations back to each sentence's TranslatedText, and leaves the timeline and word-level details unchanged.
/// Shared by TranslateCommand and the pipeline graph command for assembly.
/// </summary>
public sealed class TranslationOperator(
    ITranslationStrategy strategy,
    TranslationOptions options,
    ILogger<TranslationOperator> logger) : PipelineOperatorBase<TranslationOperator>(logger)
{
    private readonly ITranslationStrategy _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
    private readonly TranslationOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    /// <summary>Display name of the operator in the pipeline.</summary>
    public override string Name => "Translation";

    /// <summary>Runs translation and updates the workflow state (TranslatedSentences / CurrentSentences / IsTranslated).</summary>
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

    /// <summary>Computes translation QA: glossary term hit rate + translated/source length deviation (plain-text statistics, no extra LLM calls).</summary>
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
