using Centurion.Core.Workflow.Factories;using Centurion.Abstractions.Pipeline;
using Centurion.Abstractions.Strategy;
using Centurion.Abstractions.Exceptions;
using Centurion.Models;
using Centurion.Models.Workflow;
using Centurion.Core.Processing.Text;using Microsoft.Extensions.Logging;
using Centurion.Abstractions.Utils;

namespace Centurion.Core.Workflow.Pipeline.Operators;

/// <summary>
/// Pipeline operator that performs forced alignment on sentences.
/// It reads the current subtitle sentences and converted audio path from the workflow context,
/// executes the alignment strategy injected during pipeline assembly to refine word-level timestamps,
/// and writes the result back to State.AlignedSentences and State.CurrentSentences.
/// </summary>
public class AlignmentOperator : PipelineOperatorBase<AlignmentOperator>, IHealthCheckableOperator
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AlignmentOperator> _logger;
    private readonly IAlignmentStrategy _strategy;
    private readonly IToolManagerFactory _toolManagerFactory;

    /// <summary>The display name of this operator in the pipeline.</summary>
    public override string Name => "Forced Alignment";

    /// <summary>
    /// Creates an alignment operator instance.
    /// </summary>
    /// <param name="serviceProvider">The dependency-injection container used to resolve services.</param>
    /// <param name="logger">The logger that records the alignment process.</param>
    /// <param name="strategy">The resolved forced-alignment strategy.</param>
    /// <param name="toolManagerFactory">The factory used to create the CrispASR tool manager.</param>
    public AlignmentOperator(
        IServiceProvider serviceProvider,
        ILogger<AlignmentOperator> logger,
        IAlignmentStrategy strategy,
        IToolManagerFactory toolManagerFactory)
        : base(logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
        _toolManagerFactory = toolManagerFactory ?? throw new ArgumentNullException(nameof(toolManagerFactory));
    }

    /// <summary>
    /// Runs forced alignment: reads the current sentences and the converted audio, refines the
    /// word-level timestamps via the strategy, and writes the result back into the
    /// <see cref="SubtitleWorkflowContext"/> state. Keeps the original timings when alignment fails.
    /// </summary>
    /// <param name="context">The subtitle workflow context, providing sentences, audio path, and config.</param>
    /// <param name="cancellationToken">The cancellation token used to cancel the alignment process.</param>
    public override async Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken)
    {
        // Alignment is omitted from the pipeline when disabled.
        var sentences = context.State.CurrentSentences;
        if (sentences.Count == 0)
        {
            const string message = "No current sentences are available for alignment.";
            context.State.Errors.Add(message);
            _logger.LogWarning(message);
            throw new AlignmentException(message);
        }

        // 2. Validate input
        var audioPath = context.State.PreprocessedAudioPath ?? context.State.ConvertedAudioPath;
        if (string.IsNullOrEmpty(audioPath) || !File.Exists(audioPath))
        {
            throw new InvalidOperationException($"Audio file not found or not converted: {audioPath}");
        }

        // Report progress.
        OnProgress(0, "Preparing alignment...");

        // The model was validated during pipeline assembly.
        var modelName = context.Config.AlignmentModel ?? "qwen3-forced-aligner-0.6b";

        {
            var strategy = _strategy;

            var metadata = context.State.CorrectionMetadata;
            var originalTimings = sentences.ToDictionary(
                sentence => sentence,
                sentence => new SentenceTiming(
                    sentence.Start,
                    sentence.End,
                    sentence.Words.Select(word => new WordTiming(word.Start, word.End)).ToList()));

            // 7. Execute alignment (sentences keep their original text)
            LogInfo($"Using alignment model: {modelName}");
            OnProgress(30, "Running alignment...");

            List<Sentence> alignedSentences;
            try
            {
                alignedSentences = await strategy.AlignAsync(sentences, audioPath, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Forced alignment failed; keeping all original subtitle timings.");
                foreach (var sentence in sentences)
                {
                    sentence.Start = originalTimings[sentence].Start;
                    sentence.End = originalTimings[sentence].End;
                }
                alignedSentences = sentences;
            }

            if (alignedSentences.Count != sentences.Count)
            {
                var actualCount = alignedSentences?.Count ?? 0;
                var message = $"Alignment changed the sentence count from {sentences.Count} to {actualCount}.";
                _logger.LogWarning(message);
                context.State.Errors.Add(message);
                throw new AlignmentException(message);
            }

            // 8. Update context
            var alignedBySource = new Dictionary<Sentence, Sentence>();
            for (var index = 0; index < sentences.Count; index++)
            {
                var original = sentences[index];
                var aligned = alignedSentences[index];
                alignedBySource[original] = aligned;
                if (!ReferenceEquals(original, aligned) && metadata.TryGetValue(original, out var originalMetadata))
                    metadata[aligned] = originalMetadata;
                var timing = originalTimings[original];
                if (!HasTimingChange(aligned, timing))
                {
                    MarkKept(context, metadata, aligned, timing, unmatched: true);
                    continue;
                }

                var driftMs = aligned.Start - timing.Start;
                SetMetadata(metadata, aligned, CorrectKeys.DriftMs, driftMs);
                SetMetadata(metadata, aligned, CorrectKeys.Action,
                    Math.Abs(driftMs) > context.Config.MaxDriftMs ? "retimed" : "kept");
                if (Math.Abs(driftMs) > context.Config.MaxDriftMs)
                    context.State.Report.TimelineShifted++;
            }
            context.State.AlignedSentences =
            [
                .. sentences
                    .Select(sentence => alignedBySource.GetValueOrDefault(sentence, sentence))
            ];
            context.State.CurrentSentences = context.State.AlignedSentences;
            context.State.CorrectedSentences = context.State.AlignedSentences;
            context.State.IsAligned = true;

            OnProgress(100, "Alignment completed");
            LogInfo($"Alignment finished. Processed {alignedSentences.Count} sentences.");
        }
    }

    private static bool HasTimingChange(Sentence sentence, SentenceTiming original)
    {
        if (sentence.Start != original.Start || sentence.End != original.End)
            return true;

        return sentence.Words.Count != original.Words.Count ||
               sentence.Words.Where((word, index) =>
                   word.Start != original.Words[index].Start || word.End != original.Words[index].End).Any();
    }

    private static void MarkKept(
        SubtitleWorkflowContext context,
        Dictionary<Sentence, Dictionary<string, object>> metadata,
        Sentence sentence,
        SentenceTiming original,
        bool unmatched)
    {
        sentence.Start = original.Start;
        sentence.End = original.End;
        for (var index = 0; index < Math.Min(sentence.Words.Count, original.Words.Count); index++)
        {
            sentence.Words[index].Start = original.Words[index].Start;
            sentence.Words[index].End = original.Words[index].End;
        }

        SetMetadata(metadata, sentence, CorrectKeys.DriftMs, 0d);
        SetMetadata(metadata, sentence, CorrectKeys.Action, "kept");
        if (unmatched)
            context.State.Report.Unmatched++;
    }

    private static void SetMetadata(
        Dictionary<Sentence, Dictionary<string, object>> metadata,
        Sentence sentence,
        string key,
        object value)
    {
        if (!metadata.TryGetValue(sentence, out var values))
            metadata[sentence] = values = new Dictionary<string, object>();
        values[key] = value;
    }

    private sealed record SentenceTiming(double Start, double End, List<WordTiming> Words);
    private sealed record WordTiming(double Start, double End);

    /// <summary>
    /// Checks the alignment runtime environment: verifies whether the CrispASR executable is ready,
    /// and logs a warning when it is missing.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token used to cancel the health check.</param>
    public override async Task CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        LogInfo("Checking alignment environment...");

        var toolManager = _toolManagerFactory.Create("crispasr");
        if (!File.Exists(toolManager.ExecutablePath))
        {
            LogWarning($"CrispASR tool not found at: {toolManager.ExecutablePath}");
        }
        else
        {
            LogInfo($"CrispASR tool found at: {toolManager.ExecutablePath}");
        }

        await Task.CompletedTask;
    }
}
