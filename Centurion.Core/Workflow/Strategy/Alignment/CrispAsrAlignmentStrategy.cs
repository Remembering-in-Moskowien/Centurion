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
/// 基于 CrispASR 的强制对齐策略：将句子按时间间隙/时长聚合为分段块（chunk），
/// 每个块裁剪一段音频并仅启动一次对齐进程，再把词级时间戳按词数切分映射回各句。
/// 分段处理显著降低长音频的进程与模型加载开销；单块失败时保留块内各句的粗时间。
/// </summary>
public sealed class CrispAsrAlignmentStrategy(
    IModelPathResolver modelPathResolver,
    IServiceProvider serviceProvider,
    ILogger<CrispAsrAlignmentStrategy> logger,
    string modelName) : IAlignmentStrategy
{
    /// <summary>分段块内最大句子数，超过后强制另起一块。</summary>
    private const int MaxSentencesPerChunk = 50;

    /// <summary>相邻句子的时间间隙超过该秒数时切分为独立块（默认 2.0 秒）。</summary>
    public double ChunkGapSeconds { get; set; } = 2.0;

    /// <summary>单个分段块的最大音频时长（秒），超过后强制另起一块（默认 120 秒）。</summary>
    public double MaxChunkSeconds { get; set; } = 120.0;

    /// <summary>
    /// 对给定句子执行强制对齐，返回时间戳细化后的句子列表。
    /// 句子按间隙/时长聚合成块，逐块裁剪音频并对齐，词级时间戳切分回各句。
    /// </summary>
    /// <param name="sentences">待对齐的句子集合（就地更新时间戳）。</param>
    /// <param name="audioPath">对应的音频文件路径。</param>
    /// <param name="cancellationToken">用于取消对齐过程的取消标记。</param>
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
    /// 将句子按时间间隙、块时长与块内句数上限聚合为对齐分段（internal，便于单元测试）。
    /// 切分条件（满足任一即另起一块）：与上一句间隙超过 <paramref name="chunkGapSeconds"/>；
    /// 块累计时长超过 <paramref name="maxChunkSeconds"/>；块内句数达到 <paramref name="maxSentencesPerChunk"/>。
    /// 无有效时间窗口的句子（End &lt;= Start）被排除在块外。
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
    /// 把一个块的词级时间序列按各句词数切分并写回各句（internal，便于单元测试）。
    /// 时间条目不足的句子按现有容错逻辑截断（部分词保留粗时间）；无词且无文本的句子跳过。
    /// </summary>
    /// <param name="chunkSentences">块内句子（就地更新）。</param>
    /// <param name="timings">对齐器返回的词级时间（块内音频的相对秒数）。</param>
    /// <param name="offsetMilliseconds">块内音频相对原始音频的时间偏移（毫秒）。</param>
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

    /// <summary>把块内句子文本以空格连接为单行参考文本，并转义引号/反斜杠。</summary>
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

/// <summary>一个对齐分段块：块内句子列表及其覆盖的时间窗口（秒）。</summary>
internal sealed record AlignmentChunk(List<Sentence> Sentences, double StartSeconds, double EndSeconds);
