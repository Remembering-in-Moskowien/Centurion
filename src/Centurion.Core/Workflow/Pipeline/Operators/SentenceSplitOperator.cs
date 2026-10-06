using Centurion.Abstractions;
using Centurion.Abstractions.Pipeline;
using Centurion.Abstractions.Strategy;
using Centurion.Models;
using Centurion.Models.Workflow;
using Centurion.Core.Workflow.Strategy.SentenceSplit;using Microsoft.Extensions.Logging;

namespace Centurion.Core.Workflow.Pipeline.Operators;

/// <summary>
/// Sentence-splitting operator that uses the strategy and options (rule-based/LLM, etc.) injected by the pipeline assembly stage.
/// </summary>
public class SentenceSplitOperator(
    ISentenceSplitStrategy strategy,
    SplitOptions options,
    ILogger<SentenceSplitOperator> logger) : PipelineOperatorBase<SentenceSplitOperator>(logger)
{
    private readonly ISentenceSplitStrategy _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
    private readonly SplitOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    /// <summary>Display name of the operator in the pipeline.</summary>
    public override string Name => "Sentence Splitting";

    /// <summary>
    /// Runs sentence splitting: creates the split strategy via the factory as configured, splits the current word stream into sentences, and writes them back into the workflow state.
    /// </summary>
    /// <param name="context">Subtitle workflow context, providing the input word stream and split configuration.</param>
    /// <param name="cancellationToken">Token used to cancel the splitting process.</param>
    public override async Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken)
    {
        if (context.State.IsSplit)
        {
            LogInfo("Split results already exist, skipping.");
            context.State.CurrentSentences = context.State.SplitSentences;
            return;
        }

        var inputSentences = context.State.DiarizedSentences?.Count > 0
            ? context.State.DiarizedSentences
            : context.State.CoarseSentences?.Count > 0
                ? context.State.CoarseSentences
                : context.State.TranscribeSentences;

        if (inputSentences == null || inputSentences.Count == 0)
        {
            const string message = "No current sentences are available for splitting.";
            context.State.Errors.Add(message);
            LogWarning(message);
            context.State.SplitSentences = [];
            context.State.CurrentSentences = context.State.SplitSentences;
            throw new InvalidOperationException(message);
        }

        LogInfo($"Using split strategy: {_strategy.GetType().Name}");

        var allSplit = new List<Sentence>();
        foreach (var sentence in inputSentences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (sentence.Words == null || sentence.Words.Count == 0)
                continue;

            var result = await _strategy.Split(sentence.Words, _options);
            allSplit.AddRange(result);
        }

        context.State.SplitSentences = allSplit;
        context.State.CurrentSentences = context.State.SplitSentences;
        context.State.IsSplit = true;
        LogInfo($"Split into {allSplit.Count} sentences.");
    }
}
