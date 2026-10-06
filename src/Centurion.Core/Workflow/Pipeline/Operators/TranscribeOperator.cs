using Centurion.Abstractions.Pipeline;
using Centurion.Abstractions.Providers;
using Centurion.Core.Providers;
using Centurion.Models;
using Centurion.Models.Workflow;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Workflow.Pipeline.Operators;

/// <summary>
/// Transcription operator: runs speech recognition along the resolved ASR fallback chain.
/// The chain is resolved by <see cref="Providers.ProviderFactory"/> at assembly time (local/cloud mutual backup;
/// automatically falls back to local when no API key is present or the cloud fails); usage (tokens/audio seconds/estimated cost) is aggregated into
/// <see cref="WorkflowState.ProviderUsages"/>.
/// </summary>
public class TranscribeOperator(
    IReadOnlyList<IAsrProvider> chain,
    IProviderFactory providerFactory,
    ILogger<TranscribeOperator> logger) : PipelineOperatorBase<TranscribeOperator>(logger)
{
    private readonly IReadOnlyList<IAsrProvider> _chain = chain;
    private readonly IProviderFactory _providerFactory = providerFactory;
    private readonly ProviderPolicies _policies = new(providerFactory.Policies);

    /// <inheritdoc />
    public override string Name => "Transcribe";

    /// <summary>
    /// Transcription: runs along the chain, automatically skipping unavailable/failed providers and switching to backups.
    /// </summary>
    /// <param name="context">Subtitle workflow context (input audio, configuration).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="NotSupportedException">No provider supports the transcription engine (already intercepted at assembly time).</exception>
    public override async Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken)
    {
        var config = context.Config;
        var inputPath = context.State.VocalsPath
            ?? context.State.PreprocessedAudioPath
            ?? context.State.ConvertedAudioPath
            ?? config.InputFilePath;

        if (_chain.Count == 0)
            throw new NotSupportedException($"No ASR provider available for engine '{config.TranscriberEngine}'.");

        var providerResult = await ProviderChain.ExecuteAsync(
            _chain,
            (provider, ct) => ((IAsrProvider)provider).TranscribeAsync(
                inputPath, config.Language, config.TranscriberModel, config.InitialPrompt, ct),
            _policies,
            _providerFactory.Policies.BudgetUsdPerRun,
            Logger,
            cancellationToken);
        var words = providerResult.Value;
        context.State.ProviderUsages.Add(providerResult.Usage with
        {
            AudioSeconds = await ProbeAudioSecondsAsync(inputPath, cancellationToken)
        });

        if (context.State.TranscribeSentences.Count == 0)
            context.State.TranscribeSentences = GroupIntoSentences(DeduplicateWordRepeats(words));
        context.State.CurrentSentences = [.. context.State.TranscribeSentences];
        context.State.IsTranscribed = true;

        OnProgress(100, "Transcription completed.");
        LogInfo($"Transcribed {context.State.TranscribeSentences.Count} sentences " +
                $"via {providerResult.Usage.ProviderName} (est. ${providerResult.Usage.EstimatedCostUsd:F4}).");
    }

    /// <summary>
    /// Removes adjacent, fully duplicated words from the word stream (identical text and identical start/end timestamps).
    /// CrispASR's qwen3 backend emits the sentence-initial token twice when decoding segment-by-segment
    /// (same text and same timestamp; observed that almost every sentence's first word is duplicated, leaving an extra word at the start of each subtitle line);
    /// real speech never contains two words with identical timestamps, so this rule is safe and does not remove legitimate word/sentence repetition.
    /// </summary>
    /// <param name="words">Transcribed word stream (raw provider output).</param>
    /// <returns>The deduplicated word stream.</returns>
    internal static List<Word> DeduplicateWordRepeats(IReadOnlyList<Word> words)
    {
        var result = new List<Word>(words.Count);
        Word? prev = null;
        foreach (var word in words)
        {
            if (prev is not null
                && prev.Start == word.Start
                && prev.End == word.End
                && string.Equals(prev.Text, word.Text, StringComparison.Ordinal))
            {
                continue;
            }
            result.Add(word);
            prev = word;
        }
        return result;
    }

    /// <summary>Groups word-level results into sentences (heuristic splitting by punctuation; the whole segment is one sentence when there is no punctuation); sentence-level confidence is the mean of word-level confidences.</summary>
    internal static List<Sentence> GroupIntoSentences(IReadOnlyList<Word> words)
    {
        var sentences = new List<Sentence>();
        if (words.Count == 0)
            return sentences;

        Sentence? current = null;
        foreach (var word in words)
        {
            if (current is null)
            {
                // The first word directly starts a sentence (Words already contains that word); previously it was Add'ed again after ??=,
                // causing each sentence's first word to be added twice (an extra word at the start of each subtitle line); fixed.
                current = new Sentence
                {
                    Start = word.Start,
                    End = word.End,
                    Text = word.Text,
                    Words = [word]
                };
            }
            else
            {
                current.Words.Add(word);
                current.Text = string.Concat(current.Words.Select(w => w.Text));
                current.End = word.End;
            }
            if (IsSentenceBoundary(word.Text))
            {
                sentences.Add(current);
                current = null;
            }
        }
        if (current is not null)
            sentences.Add(current);

        foreach (var sentence in sentences)
            sentence.Confidence = AggregateConfidence(sentence.Words);
        return sentences;
    }

    /// <summary>Aggregates word-level confidence into sentence-level confidence (mean; returns null when all are null).</summary>
    internal static double? AggregateConfidence(IReadOnlyList<Word> words)
    {
        var values = words.Where(w => w.Confidence is not null).Select(w => w.Confidence!.Value).ToList();
        return values.Count == 0 ? null : Math.Round(values.Average(), 4);
    }

    private static bool IsSentenceBoundary(string text)
    {
        var trimmed = text.TrimEnd();
        return trimmed.Length > 0 && (trimmed.EndsWith('。') || trimmed.EndsWith('！')
            || trimmed.EndsWith('？') || trimmed.EndsWith('.') || trimmed.EndsWith('!')
            || trimmed.EndsWith('?'));
    }

    /// <summary>Probes the audio duration (seconds); returns 0 on failure (does not affect transcription results).</summary>
    private static async Task<double> ProbeAudioSecondsAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var info = await FFMpegCore.FFProbe.AnalyseAsync(path, cancellationToken: cancellationToken);
            return info.Duration.TotalSeconds;
        }
        catch
        {
            return 0;
        }
    }
}
