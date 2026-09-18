using Centurion.Core.Factories;
using Centurion.Abstractions;
using Centurion.Abstractions.Factories;
using Centurion.Abstractions.Strategy;
using Centurion.Abstractions.Exceptions;
using Centurion.Core.Managers;
using Centurion.Models;
using FFMpegCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SubtitlesParserV2;

namespace Centurion.Core.Strategy.Alignment;

public sealed class CrispAsrAlignmentStrategy(
    IModelPathResolver modelPathResolver,
    IServiceProvider serviceProvider,
    ILogger<CrispAsrAlignmentStrategy> logger,
    string modelName) : IAlignmentStrategy
{
    public async Task<List<Sentence>> AlignAsync(List<Sentence> sentences, string audioPath, CancellationToken cancellationToken)
    {
        if (sentences.Count == 0)
            return sentences;

        var expectedSentenceCount = sentences.Count;

        var toolManager = serviceProvider.GetRequiredService<IToolManagerFactory>().Create("crispasr");
        await toolManager.EnsureToolAsync(cancellationToken);
        var modelPath = await modelPathResolver.GetQwen3ForcedAlignerPathAsync(modelName, cancellationToken);
        var tempDir = Path.Combine(Path.GetTempPath(), $"crispalign_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            for (var index = 0; index < sentences.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sentence = sentences[index];
                if (sentence.End <= sentence.Start)
                {
                    logger.LogWarning("Skipping alignment for sentence {Index}: no coarse time window.", index + 1);
                    continue;
                }

                var clipStart = Math.Max(0, sentence.Start / 1000.0 - 0.5);
                var clipEnd = sentence.End / 1000.0 + 0.5;
                var clipPath = Path.Combine(tempDir, $"sentence_{index:D4}.wav");

                try
                {
                    await ExtractAudioSegmentAsync(audioPath, clipPath, clipStart, clipEnd - clipStart, cancellationToken);
                    var timings = await AlignSentenceAsync(sentence, clipPath, toolManager, modelPath, cancellationToken);
                    if (timings.Count == 0)
                    {
                        logger.LogWarning("No timings for sentence {Index}; keeping coarse timing.", index + 1);
                        continue;
                    }
                    MapTimingsToSentence(sentence, timings, clipStart * 1000);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Alignment failed for sentence {Index}; keeping coarse timing.", index + 1);
                }
            }
        }
        finally
        {
            try { Directory.Delete(tempDir, true); }
            catch { }
        }

        if (sentences.Count != expectedSentenceCount)
        {
            var message = $"Alignment changed the sentence count from {expectedSentenceCount} to {sentences.Count}.";
            logger.LogError(message);
            throw new AlignmentException(message);
        }

        return sentences;
    }

    private async Task<List<(double Start, double End)>> AlignSentenceAsync(
        Sentence sentence, string audioPath, ToolManager toolManager, string modelPath, CancellationToken cancellationToken)
    {
        var outputPath = Path.GetTempFileName();
        try
        {
            var reference = sentence.Text.Replace("\\", "\\\\").Replace("\"", "\\\"");
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
