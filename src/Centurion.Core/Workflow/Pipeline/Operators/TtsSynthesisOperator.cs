using Centurion.Abstractions.Pipeline;
using Centurion.Abstractions.Tts;
using Centurion.Models;
using Centurion.Models.Workflow;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Workflow.Pipeline.Operators;

/// <summary>
/// TTS synthesis operator (dub Phase 3): synthesizes a wav clip for each sentence by calling the TTS engine.
/// The engine is resolved once at pipeline-assembly time (by <c>DubCommand</c>) and injected through the
/// constructor; this operator no longer switches on <see cref="WorkflowConfig.TtsEngine"/> at runtime.
/// Supports parallel synthesis (bucketed by speaker; order preserved within a bucket, parallel across buckets, bounded by the <c>DubConfig.TtsParallelism</c> global limit)
/// and long-sentence chunking (when the target duration exceeds a threshold, splits the text into sub-segments proportionally, places them contiguously, and splices them in the mixing stage).
/// A failed single-sentence synthesis logs a Warning and marks it Skipped, without blocking the whole pipeline.
/// Results are written to State.DubSegments (List&lt;DubSegment&gt;, in original sentence order).
/// </summary>
public sealed class TtsSynthesisOperator(
    ITtsEngine engine,
    ILogger<TtsSynthesisOperator> logger)
    : PipelineOperatorBase<TtsSynthesisOperator>(logger)
{
    private readonly ITtsEngine _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    /// <summary>Maximum characters per sentence: longer text is truncated (to avoid TTS failures on overly long sentences).</summary>
    private const int MaxCharsPerSentence = 200;

    /// <summary>Operator name.</summary>
    public override string Name => "TTS Synthesis";

    /// <summary>
    /// Synthesizes all sentences: first splits long sentences into multiple work items (chunking) by target duration/character count,
    /// then synthesizes in parallel grouped by speaker (same speaker serialized in order), and finally reassembles DubSegments in original sentence order.
    /// </summary>
    /// <param name="context">Workflow context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public override async Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken)
    {
        var sentences = context.State.CurrentSentences;
        if (sentences.Count == 0)
        {
            LogWarning("No sentences to synthesize.");
            return;
        }

        var engine = _engine;
        var references = context.State.DubSpeakerReferences;

        var parallelism = Math.Max(1, context.Config.TtsParallelism);
        var maxChunkSeconds = Math.Max(5, context.Config.DubMaxChunkSeconds);
        var tempDir = context.State.PipelineTempDirectory ?? throw new InvalidOperationException("Pipeline temp directory is not initialized.");

        // 1) Build work items (long-sentence chunking): keep the original order index
        var workItems = new List<WorkItem>();
        for (var i = 0; i < sentences.Count; i++)
        {
            var sentence = sentences[i];
            var text = (sentence.TranslatedText ?? sentence.Text).Trim();
            if (text.Length == 0)
                continue;

            // sentence.Start/End are seconds; DubSegment.TargetStartMs/TargetEndMs are milliseconds.
            var targetSec = Math.Max(0, sentence.End - sentence.Start);
            var chunks = SplitLongSentence(text, targetSec * 1000, maxChunkSeconds);
            var chunkDurationSec = targetSec / (double)chunks.Count;
            for (var c = 0; c < chunks.Count; c++)
            {
                workItems.Add(new WorkItem
                {
                    SentenceIndex = i,
                    Text = chunks[c],
                    Speaker = sentence.Speaker,
                    Reference = sentence.Speaker != null && references.TryGetValue(sentence.Speaker, out var r) ? r : null,
                    TargetStartMs = (sentence.Start + c * chunkDurationSec) * 1000,
                    TargetEndMs = (c == chunks.Count - 1 ? sentence.End : sentence.Start + (c + 1) * chunkDurationSec) * 1000
                });
            }
        }

        // 2) Bucket by speaker (null speakers merged into one bucket); order preserved within each bucket
        var buckets = workItems
            .GroupBy(w => w.Speaker ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderBy(w => w.SentenceIndex).ThenBy(w => w.TargetStartMs).ToList())
            .ToList();

        var segments = new List<DubSegment>();
        using var gate = new SemaphoreSlim(parallelism, parallelism);
        var lockObject = new object();

        // 3) Synthesize in parallel across buckets; serialized within a bucket (in order)
        var bucketTasks = buckets.Select(async bucket =>
        {
            foreach (var item in bucket)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var segment = new DubSegment
                {
                    Text = item.Text,
                    SpeakerId = item.Speaker,
                    ReferenceAudioPath = item.Reference,
                    TargetStartMs = item.TargetStartMs,
                    TargetEndMs = item.TargetEndMs,
                    SynthesizedWavPath = Path.Combine(tempDir, $"dub_{item.SentenceIndex:D4}_{item.TargetStartMs:000000}.wav")
                };

                await gate.WaitAsync(cancellationToken);
                try
                {
                    segment.SynthesizedDurationSec = await engine.SynthesizeAsync(
                        item.Text, item.Reference, context.Config.TtsLanguage, segment.SynthesizedWavPath!, cancellationToken);
                }
                catch (TtsSynthesisException ex)
                {
                    segment.Skipped = true;
                    segment.Note = ex.Message;
                    LogWarning($"TTS failed for sentence {item.SentenceIndex + 1}: {ex.Message}");
                }
                catch (Exception ex)
                {
                    segment.Skipped = true;
                    segment.Note = ex.Message;
                    LogWarning($"TTS error for sentence {item.SentenceIndex + 1}: {ex.Message}");
                }
                finally
                {
                    gate.Release();
                }

                lock (lockObject)
                {
                    segments.Add(segment);
                }
            }
        });

        await Task.WhenAll(bucketTasks);

        // 4) Reassemble in original order (sentence index ascending; sub-segments within a sentence ordered by time)
        var finalSegments = segments
            .OrderBy(s => GetSentenceOrder(s.TargetStartMs, sentences))
            .ThenBy(s => s.TargetStartMs)
            .ToList();

        context.State.DubSegments = finalSegments;
        var skipped = finalSegments.Count(s => s.Skipped);
        OnProgress(100, $"Synthesized {finalSegments.Count - skipped}/{finalSegments.Count} segments");
        LogInfo($"TTS synthesis completed: {finalSegments.Count} segments ({buckets.Count} speaker groups, parallelism {parallelism}), {skipped} skipped.");
    }

    /// <summary>
    /// Maps a sentence back to its original order index by matching the target start time (used to restore original order after parallel synthesis).
    /// </summary>
    private static int GetSentenceOrder(double targetStartMs, List<Sentence> sentences)
    {
        for (var i = 0; i < sentences.Count; i++)
        {
            // sentences[].Start is seconds; targetStartMs is milliseconds.
            if (Math.Abs(sentences[i].Start * 1000 - targetStartMs) < 1)
                return i;
        }
        return sentences.Count;
    }

    /// <summary>
    /// Long-sentence chunking: when the target duration exceeds maxChunkSeconds, splits the text into sub-segments proportionally by duration.
    /// Chinese/Japanese split by character; others split by whitespace/punctuation; sub-segment count has a lower bound of 1 and an upper bound of 8.
    /// </summary>
    /// <param name="text">Target text.</param>
    /// <param name="targetMs">Target duration (milliseconds).</param>
    /// <param name="maxChunkSeconds">Maximum target duration per chunk (seconds).</param>
    /// <returns>The list of sub-segments (at least one item).</returns>
    internal static List<string> SplitLongSentence(string text, double targetMs, double maxChunkSeconds)
    {
        if (targetMs <= 0 || targetMs / 1000.0 <= maxChunkSeconds || text.Length <= 2)
            return [text];

        var chunkCount = Math.Clamp((int)Math.Ceiling(targetMs / 1000.0 / maxChunkSeconds), 2, 8);
        if (text.Length <= chunkCount)
            return [text];

        return SplitText(text, chunkCount);
    }

    /// <summary>
    /// Splits the text into n chunks as evenly as possible by character count, preferring breaks at whitespace/punctuation (English); CJK is split directly by character.
    /// </summary>
    /// <param name="text">Input text.</param>
    /// <param name="chunkCount">Target number of chunks.</param>
    /// <returns>The split sub-segments.</returns>
    internal static List<string> SplitText(string text, int chunkCount)
    {
        var isCjk = text.Any(ch => ch > 0x2E80);
        var results = new List<string>();

        if (!isCjk)
        {
            // English and other whitespace-script languages: prefer breaking at punctuation/whitespace
            var tokens = text.Split(' ');
            var targetPerChunk = Math.Ceiling(tokens.Length / (double)chunkCount);
            var current = new List<string>();
            foreach (var token in tokens)
            {
                current.Add(token);
                if (current.Count >= targetPerChunk)
                {
                    results.Add(string.Join(' ', current).Trim());
                    current.Clear();
                }
            }
            if (current.Count > 0)
                results.Add(string.Join(' ', current).Trim());
            if (results.Count == 1 && results[0].Length == 0)
                results = [text];
            return results;
        }

        // CJK: split evenly by character
        var perChunk = Math.Max(1, (int)Math.Ceiling(text.Length / (double)chunkCount));
        for (var i = 0; i < text.Length; i += perChunk)
            results.Add(text.Substring(i, Math.Min(perChunk, text.Length - i)));
        return results;
    }

    /// <summary>Internal work item: sentence index + target text + speaker/reference + target time window.</summary>
    private sealed class WorkItem
    {
        public int SentenceIndex { get; init; }
        public string Text { get; init; } = string.Empty;
        public string? Speaker { get; init; }
        public string? Reference { get; init; }
        public double TargetStartMs { get; init; }
        public double TargetEndMs { get; init; }
    }
}
