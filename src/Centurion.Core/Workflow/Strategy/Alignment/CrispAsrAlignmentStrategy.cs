using Centurion.Core.Workflow.Factories;using Centurion.Abstractions;
using Centurion.Abstractions.Factories;
using Centurion.Abstractions.Strategy;
using Centurion.Abstractions.Exceptions;
using Centurion.Models;
using FFMpegCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SubtitlesParserV2;
using Centurion.Abstractions.Utils;
using Centurion.Core.Capabilities.Managers.Runtime;
using Centurion.Core.Capabilities.Managers.Tools;
namespace Centurion.Core.Workflow.Strategy.Alignment;

/// <summary>
/// Forced alignment strategy based on CrispASR: aggregates sentences into chunks by time gap or duration,
/// clips one audio segment per chunk, runs the alignment process once, then splits word-level timestamps
/// back across sentences by word count. Chunked processing cuts overhead; on failure, coarse timings are kept.
/// </summary>
public sealed class CrispAsrAlignmentStrategy(
    IModelPathResolver modelPathResolver,
    IServiceProvider serviceProvider,
    ILogger<CrispAsrAlignmentStrategy> logger,
    string modelName) : IAlignmentStrategy
{
    /// <summary>Display name of the alignment strategy.</summary>
    public string StrategyName => $"CrispASR Alignment ({modelName})";

    /// <summary>The alignment stage itself produces aligned timestamps; no extra capability is declared.</summary>
    public StrategyCapabilities Capabilities => StrategyCapabilities.None;

    /// <summary>Maximum number of sentences per chunk; a new chunk is forced once this is exceeded.</summary>
    private const int MaxSentencesPerChunk = 50;

    /// <summary>Split into a new chunk when the time gap between adjacent sentences exceeds this many seconds (default 2.0 seconds).</summary>
    public double ChunkGapSeconds { get; set; } = 2.0;

    /// <summary>Maximum audio duration (seconds) per chunk; a new chunk is forced once this is exceeded (default 120 seconds).</summary>
    public double MaxChunkSeconds { get; set; } = 120.0;

    /// <summary>
    /// Performs forced alignment on the given sentences and returns the list with refined timestamps.
    /// Sentences are aggregated into chunks by gap or duration; audio is clipped and aligned per chunk, then word-level timings are split back across the sentences.
    /// </summary>
    /// <param name="sentences">The sentences to align; their timestamps are updated in place.</param>
    /// <param name="audioPath">Path to the corresponding audio file.</param>
    /// <param name="cancellationToken">Token used to cancel the alignment process.</param>
    public async Task<List<Sentence>> AlignAsync(List<Sentence> sentences, string audioPath, CancellationToken cancellationToken)
    {
        if (sentences.Count == 0)
            return sentences;

        var expectedSentenceCount = sentences.Count;

        var toolManager = serviceProvider.GetRequiredService<IToolManagerFactory>().Create("crispasr");
        await toolManager.EnsureToolAsync(cancellationToken);
        var modelPath = await modelPathResolver.GetQwen3ForcedAlignerPathAsync(modelName, cancellationToken);
        var tempHandle = await serviceProvider.GetRequiredService<ITempDirectoryManager>().CreateTempDirectoryAsync("crispalign_");
        var tempDir = tempHandle.Path;

        try
        {
            var chunks = BuildChunks(sentences, MaxChunkSeconds, ChunkGapSeconds, MaxSentencesPerChunk);
            for (var chunkIndex = 0; chunkIndex < chunks.Count; chunkIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var chunk = chunks[chunkIndex];
                if (chunk.Sentences.Count == 0)
                    continue;

                var clipStart = Math.Max(0, chunk.StartSeconds - 0.5);
                var clipEnd = chunk.EndSeconds + 0.5;
                var clipPath = Path.Combine(tempDir, $"chunk_{chunkIndex:D4}.wav");

                try
                {
                    await ExtractAudioSegmentAsync(audioPath, clipPath, clipStart, clipEnd - clipStart, cancellationToken);
                    var reference = BuildReferenceText(chunk.Sentences);
                    var timings = await AlignChunkAsync(reference, clipPath, toolManager, modelPath, cancellationToken);
                    if (timings.Count == 0)
                    {
                        logger.LogWarning(
                            "No timings for alignment chunk {Index}; keeping coarse timings for {Count} sentences.",
                            chunkIndex + 1, chunk.Sentences.Count);
                        continue;
                    }
                    SplitTimingsAcrossSentences(chunk.Sentences, timings, clipStart * 1000);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(
                        ex,
                        "Alignment failed for chunk {Index}; keeping coarse timings for {Count} sentences.",
                        chunkIndex + 1, chunk.Sentences.Count);
                }
            }
        }
        finally
        {
            await tempHandle.DisposeAsync();
        }

        if (sentences.Count != expectedSentenceCount)
        {
            var message = $"Alignment changed the sentence count from {expectedSentenceCount} to {sentences.Count}.";
            logger.LogWarning(message);
            throw new AlignmentException(message);
        }

        return sentences;
    }

    /// <summary>
    /// Aggregates sentences into chunks by time gap, chunk duration, and per-chunk sentence cap (internal, for unit testing).
    /// Split conditions (any one starts a new chunk): gap from the previous sentence exceeds <paramref name="chunkGapSeconds"/>;
    /// accumulated duration exceeds <paramref name="maxChunkSeconds"/>; sentence count reaches <paramref name="maxSentencesPerChunk"/>.
    /// Sentences with no valid time window (End &lt;= Start) are excluded from the chunks.
    /// </summary>
    internal static List<AlignmentChunk> BuildChunks(
        List<Sentence> sentences,
        double maxChunkSeconds = 120.0,
        double chunkGapSeconds = 2.0,
        int maxSentencesPerChunk = 50)
    {
        var chunks = new List<AlignmentChunk>();
        var current = new List<Sentence>();
        double chunkStartSec = 0;
        double chunkEndSec = 0;

        foreach (var sentence in sentences)
        {
            if (sentence.End <= sentence.Start)
                continue;

            var startSec = Math.Max(0, sentence.Start / 1000.0);
            var endSec = sentence.End / 1000.0;

            if (current.Count > 0)
            {
                var gapSeconds = startSec - chunkEndSec;
                var chunkDuration = endSec - chunkStartSec;
                if (gapSeconds > chunkGapSeconds
                    || chunkDuration > maxChunkSeconds
                    || current.Count >= maxSentencesPerChunk)
                {
                    chunks.Add(new AlignmentChunk([.. current], chunkStartSec, chunkEndSec));
                    current = [];
                    chunkStartSec = startSec;
                }
            }
            else
            {
                chunkStartSec = startSec;
            }

            current.Add(sentence);
            chunkEndSec = endSec;
        }

        if (current.Count > 0)
            chunks.Add(new AlignmentChunk([.. current], chunkStartSec, chunkEndSec));

        return chunks;
    }

    /// <summary>
    /// Splits a chunk's word-level timings across its sentences by word count and writes them back (internal, for unit testing).
    /// Sentences with insufficient timing entries are truncated by existing fallback logic (some words keep coarse timings); sentences with no words and no text are skipped.
    /// </summary>
    /// <param name="chunkSentences">The sentences within the chunk (updated in place).</param>
    /// <param name="timings">Word-level timings returned by the aligner, in relative seconds within the chunk audio.</param>
    /// <param name="offsetMilliseconds">Time offset of the chunk audio relative to the original audio, in milliseconds.</param>
    internal static void SplitTimingsAcrossSentences(
        List<Sentence> chunkSentences,
        List<(double Start, double End)> timings,
        double offsetMilliseconds)
    {
        var cursor = 0;
        foreach (var sentence in chunkSentences)
        {
            if (cursor >= timings.Count)
                break;

            var wordCount = sentence.Words.Count(word => word.Status != MappingStatus.AudioExtra);
            if (wordCount == 0 && !string.IsNullOrWhiteSpace(sentence.Text))
                wordCount = sentence.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
            if (wordCount <= 0)
                continue;

            var take = Math.Min(wordCount, timings.Count - cursor);
            var subTimings = timings.GetRange(cursor, take);
            cursor += take;

            if (subTimings.Count > 0)
                MapTimingsToSentence(sentence, subTimings, offsetMilliseconds);
        }
    }

    /// <summary>Joins the chunk's sentence texts with spaces into a single-line reference text, escaping quotes and backslashes.</summary>
    private static string BuildReferenceText(List<Sentence> chunkSentences) =>
        string.Join(" ", chunkSentences
            .Where(sentence => !string.IsNullOrWhiteSpace(sentence.Text))
            .Select(sentence => sentence.Text
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")));

    private async Task<List<(double Start, double End)>> AlignChunkAsync(
        string reference, string audioPath, ToolManager toolManager, string modelPath, CancellationToken cancellationToken)
    {
        var outputPath = Path.Combine(Path.GetDirectoryName(audioPath) ?? string.Empty,
            Path.GetFileNameWithoutExtension(audioPath) + ".timings.srt");
        try
        {
            var arguments = $"--align-only -am \"{modelPath}\" -f \"{audioPath}\" --ref-text \"{reference}\" --align-format srt --align-output \"{outputPath}\"";
            var processManager = serviceProvider.GetRequiredService<ProcessManager>();
            await processManager.ExecuteAsync(toolManager.ExecutablePath, arguments, cancellationToken);
            if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
                return [];

            using var stream = File.OpenRead(outputPath);
            var subtitle = SubtitleParser.ParseStream(stream);
            return subtitle?.Subtitles?
                .Select(item => (Start: (double)item.StartTime / 1000, End: (double)item.EndTime / 1000))
                .ToList() ?? [];
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }

    private static async Task ExtractAudioSegmentAsync(string source, string destination, double startSeconds, double durationSeconds, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await FFMpegArguments
            .FromFileInput(source)
            .OutputToFile(destination, true, options => options
                .WithCustomArgument($"-ss {startSeconds:0.000} -t {durationSeconds:0.000}")
                .WithAudioCodec("pcm_s16le")
                .WithAudioSamplingRate(16000)
                .WithCustomArgument("-ac 1")
                .ForceFormat("wav"))
            .ProcessAsynchronously();
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static void MapTimingsToSentence(Sentence sentence, List<(double Start, double End)> timings, double offsetMilliseconds)
    {
        if (sentence.Words.Count == 0)
        {
            var generatedWords = sentence.Text
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Select(text => new Word
                {
                    Text = text,
                    Start = sentence.Start,
                    End = sentence.End,
                    Speaker = "UNKNOWN",
                    Status = MappingStatus.Matched
                })
                .ToList();
            sentence.Words = generatedWords;
        }

        var words = sentence.Words.Where(word => word.Status != MappingStatus.AudioExtra).ToList();
        var count = Math.Min(words.Count, timings.Count);
        for (var index = 0; index < count; index++)
        {
            words[index].Start = timings[index].Start * 1000 + offsetMilliseconds;
            words[index].End = timings[index].End * 1000 + offsetMilliseconds;
        }
        if (count > 0)
        {
            sentence.Start = sentence.Words.Min(word => word.Start);
            sentence.End = sentence.Words.Max(word => word.End);
        }
    }
}

/// <summary>An alignment chunk: the list of sentences in the chunk and the time window (seconds) it covers.</summary>
internal sealed record AlignmentChunk(List<Sentence> Sentences, double StartSeconds, double EndSeconds);
