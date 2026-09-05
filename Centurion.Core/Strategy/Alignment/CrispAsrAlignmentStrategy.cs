// File: Centurion.Core.Strategy.Alignment/CrispAsrAlignmentStrategy.cs
using Centurion.Core.Abstractions;
using Centurion.Core.Abstractions.Strategy;
using Centurion.Core.Managers;
using Centurion.Core.Models;
using FFMpegCore;
using FFMpegCore.Extend;
using Microsoft.Extensions.Logging;
using SubtitlesParserV2;
using System.Text.RegularExpressions;

namespace Centurion.Core.Strategy.Alignment;

/// <summary>
/// Forced alignment strategy using the CrispASR external tool.
/// Automatically splits long audio into chunks (default 240s) to avoid drift.
/// </summary>
public class CrispAsrAlignmentStrategy(
    IModelPathResolver modelPathResolver,
    IServiceProvider serviceProvider,
    ILogger<CrispAsrAlignmentStrategy> logger,
    string modelName,
    double maxChunkSeconds = 240.0) // 默认 4 分钟
    : IAlignmentStrategy
{
    private const string AlignFormat = "srt";
    private readonly double _maxChunkSeconds = maxChunkSeconds;

    public async Task<List<Sentence>> AlignAsync(
        List<Sentence> sentences,
        string audioPath,
        CancellationToken cancellationToken)
    {
        if (sentences.Count == 0)
            return sentences;

        // 1. Ensure tool is installed
        var toolManager = new ToolManager("crispasr", serviceProvider);
        await toolManager.EnsureToolAsync(cancellationToken);

        // 2. Get model path
        var modelPath = await modelPathResolver.GetQwen3ForcedAlignerPathAsync(modelName, cancellationToken);
        logger.LogInformation("Using alignment model: {ModelPath}", modelPath);

        // 3. Get total audio duration
        var mediaInfo = await FFProbe.AnalyseAsync(audioPath, cancellationToken: cancellationToken);
        var totalDuration = mediaInfo.Duration.TotalSeconds;

        // 4. If audio is short enough, align directly
        if (totalDuration <= _maxChunkSeconds)
        {
            logger.LogInformation("Audio duration {Duration:F1}s <= {MaxChunk:F1}s, processing as single chunk.", totalDuration, _maxChunkSeconds);
            return await AlignChunkAsync(sentences, audioPath, 0.0, toolManager, modelPath, cancellationToken);
        }

        // 5. Split sentences into chunks
        logger.LogInformation("Audio duration {Duration:F1}s exceeds chunk limit {MaxChunk:F1}s. Splitting into chunks.", totalDuration, _maxChunkSeconds);
        var chunks = SplitIntoSentenceChunks(sentences, _maxChunkSeconds);

        // 6. Process each chunk and collect results
        var allAligned = new List<Sentence>();
        var tempDir = Path.Combine(Path.GetTempPath(), $"crispalign_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            for (int i = 0; i < chunks.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var chunk = chunks[i];
                var chunkStartTime = chunk.First().Start;
                var chunkEndTime = chunk.Last().End;
                var chunkDuration = chunkEndTime - chunkStartTime;

                // Extract audio segment using FFMpegCore
                var chunkAudioPath = Path.Combine(tempDir, $"chunk_{i:D4}.wav");
                await ExtractAudioSegmentAsync(audioPath, chunkAudioPath, chunkStartTime, chunkDuration, cancellationToken);

                logger.LogInformation("Processing chunk {ChunkIndex}/{TotalChunks}: duration {Duration:F1}s, sentences {Count}",
                    i + 1, chunks.Count, chunkDuration, chunk.Count);

                var alignedChunk = await AlignChunkAsync(chunk, chunkAudioPath, chunkStartTime, toolManager, modelPath, cancellationToken);
                allAligned.AddRange(alignedChunk);
            }
        }
        finally
        {
            try { Directory.Delete(tempDir, true); }
            catch { /* ignore */ }
        }

        // 7. Merge and reorder
        var merged = allAligned.OrderBy(s => s.Start).ToList();
        return merged;
    }

    /// <summary>
    /// Splits sentences into chunks based on maximum duration, respecting sentence boundaries.
    /// </summary>
    private List<List<Sentence>> SplitIntoSentenceChunks(List<Sentence> sentences, double maxDuration)
    {
        var chunks = new List<List<Sentence>>();
        var currentChunk = new List<Sentence>();
        double currentDuration = 0.0;

        foreach (var sentence in sentences)
        {
            var sentenceDuration = sentence.End - sentence.Start;
            if (currentDuration + sentenceDuration > maxDuration && currentChunk.Count > 0)
            {
                chunks.Add(currentChunk);
                currentChunk = new List<Sentence>();
                currentDuration = 0.0;
            }
            currentChunk.Add(sentence);
            currentDuration += sentenceDuration;
        }

        if (currentChunk.Count > 0)
            chunks.Add(currentChunk);

        return chunks;
    }

    /// <summary>
    /// Aligns a single chunk (audio segment + corresponding sentences).
    /// </summary>
    private async Task<List<Sentence>> AlignChunkAsync(
        List<Sentence> sentences,
        string audioPath,
        double timeOffset,
        ToolManager toolManager,
        string modelPath,
        CancellationToken cancellationToken)
    {
        // Clear any existing words
        foreach (var s in sentences)
            s.Words.Clear();

        // Build normalized reference text
        string fullText = BuildNormalizedReferenceText(sentences);
        logger.LogDebug("Chunk reference text length: {Length} characters", fullText.Length);

        // Temp output file
        var tempOutput = Path.GetTempFileName();

        try
        {
            // Build command
            var arguments = $"--align-only " +
                            $"-am \"{modelPath}\" " +
                            $"-f \"{audioPath}\" " +
                            $"--ref-text \"{fullText}\" " +
                            $"--align-format {AlignFormat} " +
                            $"--align-output \"{tempOutput}\"";

            logger.LogDebug("Executing CrispASR for chunk: {Executable} {Arguments}", toolManager.ExecutablePath, arguments);

            // Execute
            var processManager = new ProcessManager(logger);
            _ = await processManager.ExecuteAsync(
                toolManager.ExecutablePath,
                arguments,
                timeoutMs: 300000,
                cancellationToken);

            // Verify output
            if (!File.Exists(tempOutput) || new FileInfo(tempOutput).Length == 0)
            {
                logger.LogError("Alignment output file is missing or empty: {TempOutput}", tempOutput);
                return sentences;
            }

            // Parse SRT
            var wordTimings = ParseSrtOutput(tempOutput);
            logger.LogInformation("Parsed {Count} word timings from chunk SRT.", wordTimings.Count);

            if (wordTimings.Count == 0)
            {
                logger.LogWarning("No word timings produced for chunk.");
                return sentences;
            }

            // Map timings with offset
            MapTimingsToWords(sentences, wordTimings, timeOffset);
            logger.LogInformation("Chunk alignment mapping completed.");
        }
        finally
        {
            if (File.Exists(tempOutput))
                File.Delete(tempOutput);
        }

        return sentences;
    }

    /// <summary>
    /// Extracts an audio segment using FFmpeg.
    /// </summary>
    private static async Task ExtractAudioSegmentAsync(string source, string dest, double startSeconds, double durationSeconds, CancellationToken ct)
    {
        await FFMpegArguments
            .FromFileInput(source)
            .OutputToFile(dest, true, options =>
                options
                    .WithCustomArgument($"-ss {startSeconds:0.000} -t {durationSeconds:0.000}")
                    .WithAudioCodec("pcm_s16le")
                    .WithAudioSamplingRate(16000)
                    .WithCustomArgument("-ac 1")
                    .ForceFormat("wav"))
            .ProcessAsynchronously();
    }

    /// <summary>
    /// Builds normalized reference text.
    /// </summary>
    private static string BuildNormalizedReferenceText(List<Sentence> sentences)
    {
        string raw = string.Join(" ", sentences.Select(s => s.CleanedText ?? s.Text));
        return NormalizeText(raw);
    }

    private static string NormalizeText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        string normalized = text.ToLowerInvariant();
        normalized = Regex.Replace(normalized, @"[^\w\s\-']", " ");
        normalized = Regex.Replace(normalized, @"\s+", " ").Trim();
        return normalized;
    }

    /// <summary>
    /// Parses SRT output.
    /// </summary>
    private List<(string word, double start, double end)> ParseSrtOutput(string outputPath)
    {
        var results = new List<(string, double, double)>();

        try
        {
            using var stream = File.OpenRead(outputPath);
            var subtitle = SubtitleParser.ParseStream(stream);
            if (subtitle?.Subtitles == null)
                return results;

            foreach (var item in subtitle.Subtitles)
            {
                string wordText = string.Join(" ", item.Lines).Trim();
                if (string.IsNullOrEmpty(wordText))
                    continue;

                double start = item.StartTime;
                double end = item.EndTime;
                results.Add((wordText, start, end));
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse SRT output: {OutputPath}", outputPath);
        }

        return results;
    }

    /// <summary>
    /// Maps word timings to original words, applying a time offset.
    /// </summary>
    private void MapTimingsToWords(List<Sentence> sentences, List<(string word, double start, double end)> wordTimings, double offset)
    {
        int wordIdx = 0;

        foreach (var sentence in sentences)
        {
            sentence.Words.Clear();
            var originalWords = sentence.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var cleanedWords = (sentence.CleanedText ?? sentence.Text)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var wordsToMap = Math.Min(cleanedWords.Length, wordTimings.Count - wordIdx);

            for (var originalIndex = 0; originalIndex < originalWords.Length; originalIndex++)
            {
                if (wordIdx >= wordTimings.Count || wordsToMap == 0)
                {
                    logger.LogWarning("Not enough timings for all words. Missing word: {Word}", originalWords[originalIndex]);
                    break;
                }

                var groupStart = wordIdx + originalIndex * wordsToMap / originalWords.Length;
                var groupEnd = wordIdx + (originalIndex + 1) * wordsToMap / originalWords.Length;
                groupEnd = Math.Max(groupEnd, groupStart + 1);
                groupEnd = Math.Min(groupEnd, wordIdx + wordsToMap);

                if (groupStart >= groupEnd)
                    continue;

                var start = wordTimings[groupStart].start + offset;
                var end = wordTimings[groupEnd - 1].end + offset;
                sentence.Words.Add(new Word
                {
                    Text = originalWords[originalIndex],
                    Start = start,
                    End = end,
                    Speaker = "UNKNOWN"
                });
            }

            wordIdx += wordsToMap;

            if (sentence.Words.Any())
            {
                sentence.Start = sentence.Words.Min(w => w.Start);
                sentence.End = sentence.Words.Max(w => w.End);
            }
        }

        if (wordIdx < wordTimings.Count)
        {
            logger.LogWarning("There are {Extra} extra word timings not mapped in this chunk.", wordTimings.Count - wordIdx);
        }
    }
}