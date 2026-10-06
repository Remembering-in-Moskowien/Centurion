using Centurion.Abstractions.Pipeline;
using Centurion.Abstractions.Strategy;
using Centurion.Models;
using Centurion.Models.Workflow;
using Centurion.Core.Workflow.Strategy.Diarization;using Microsoft.Extensions.Logging;
using Centurion.Core.Utils.Parsing;
namespace Centurion.Core.Workflow.Pipeline.Operators;

/// <summary>
/// Speaker-diarization operator: matches each Word in the current working set to a speaker
/// segment by its time midpoint and annotates Speaker accordingly.
/// The backend is selected by WorkflowConfig.DiarizationBackend ("crispasr" / "pyannote" / "none").
/// Non-fatal errors are only logged as warnings and do not interrupt the pipeline.
/// </summary>
public sealed class DiarizationOperator(
    IDiarizationStrategy strategy,
    ILogger<DiarizationOperator> logger) : PipelineOperatorBase<DiarizationOperator>(logger)
{
    private readonly IDiarizationStrategy _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));

    /// <summary>Display name of the operator in the pipeline.</summary>
    public override string Name => "Speaker Diarization";

    /// <summary>
    /// Runs speaker diarization: creates the strategy per config, performs speaker separation
    /// on the audio, and annotates each word with the corresponding speaker by time midpoint.
    /// Failures are non-fatal and only logged as warnings.
    /// </summary>
    /// <param name="context">Subtitle workflow context, providing audio, sentences, and configuration.</param>
    /// <param name="cancellationToken">Cancellation token used to cancel the diarization process.</param>
    public override async Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken)
    {
        var config = context.Config;

        // Audio path (prefer the vocals track, then the preprocessed audio)
        var audioPath = context.State.VocalsPath
            ?? context.State.PreprocessedAudioPath
            ?? context.State.ConvertedAudioPath
            ?? config.InputFilePath;
        if (string.IsNullOrEmpty(audioPath) || !File.Exists(audioPath))
        {
            LogWarning($"Audio file unavailable for diarization: {audioPath}");
            return;
        }

        // Sentences to annotate (current working set, falling back to the transcription result)
        var sentences = context.State.CurrentSentences.Count > 0
            ? context.State.CurrentSentences
            : context.State.TranscribeSentences;
        if (sentences.Count == 0)
        {
            LogInfo("No sentences to annotate; skipping diarization.");
            return;
        }

        try
        {
            OnProgress(10, "Running speaker diarization");
            var turns = await _strategy.DiarizeAsync(
                audioPath, config.NumSpeakers, config.DiarizationModel, cancellationToken, config.Device);

            if (turns.Count == 0)
            {
                LogWarning("Diarization returned no speaker segments.");
                return;
            }

            // 6. Post-processing smoothing: merge adjacent same-speaker segments, remove
            //    per-segment alternation jitter and overly short fragments,
            //    significantly reducing the mislabeling rate of boundary words.
            var smoothed = SpeakerSegmentSmoother.Smooth(turns, config.DiarizationMinSegmentSeconds);
            if (smoothed.Count < turns.Count)
                LogInfo($"Speaker segments smoothed from {turns.Count} to {smoothed.Count} (min duration {config.DiarizationMinSegmentSeconds}s).");

            // 7. Map speakers onto each Word by time-window overlap maximization (Words are reference types; annotated in place)
            var annotated = sentences.ToList();
            var annotatedWordCount = 0;
            foreach (var sentence in annotated)
            {
                foreach (var word in sentence.Words)
                {
                    word.Speaker = ResolveSpeaker(word, smoothed);
                    annotatedWordCount++;
                }
            }

            context.State.DiarizedSentences = annotated;
            context.State.IsDiarized = true;
            OnProgress(100, "Diarization completed");
            LogInfo($"Assigned speakers to {annotatedWordCount} words across {smoothed.Count} segments.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Non-fatal: a speaker-annotation failure does not interrupt subtitle generation
            LogWarning($"Speaker diarization failed; continuing without speaker labels. {ex.Message}");
        }
    }

    /// <summary>
    /// Assigns a speaker to a Word by the overlap between the word's time window and each speaker segment (internal, for unit testing).
    /// Picks the segment with the longest intersection; when a word straddles a speaker switch it goes to the side with more overlap,
    /// avoiding the whole-word mislabeling the time-midpoint rule causes at boundaries. With no overlap at all (degenerate word timestamps) it falls back to
    /// the nearest segment by time; with no segments at all it falls back to the default label.
    /// </summary>
    internal static string ResolveSpeaker(Word word, IReadOnlyList<SpeakerSegment> turns)
    {
        if (word == null || turns == null || turns.Count == 0)
            return "SPEAKER_00";

        var bestOverlap = 0.0;
        SpeakerSegment? best = null;
        foreach (var turn in turns)
        {
            var startMs = turn.StartSeconds * 1000.0;
            var endMs = turn.EndSeconds * 1000.0;
            var overlap = Math.Min(word.End, endMs) - Math.Max(word.Start, startMs);
            if (overlap > bestOverlap)
            {
                bestOverlap = overlap;
                best = turn;
            }
        }

        if (best is not null)
            return best.Speaker;

        // The word window has no overlap with any segment (degenerate word timestamps or a gap
        // between segments): take the nearest segment by time, avoiding mass fallback to the placeholder label that would lose speaker information.
        SpeakerSegment? nearest = null;
        var nearestDistance = double.MaxValue;
        foreach (var turn in turns)
        {
            var startMs = turn.StartSeconds * 1000.0;
            var endMs = turn.EndSeconds * 1000.0;
            var distance = word.End < startMs ? startMs - word.End : word.Start - endMs;
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = turn;
            }
        }

        return nearest?.Speaker ?? "SPEAKER_00";
    }
}
