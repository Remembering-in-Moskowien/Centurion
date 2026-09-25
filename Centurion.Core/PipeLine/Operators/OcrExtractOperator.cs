using Centurion.Abstractions.Pipeline;
using Centurion.Models;
using Centurion.Models.Workflow;
using Centurion.Core.Ocr;
using Centurion.Core.Utils;
using FFMpegCore;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Pipeline.Operators;

/// <summary>
/// OCR 提取算子（spawn --mode ocr）：用 GLM-OCR 从视频帧/图片中提取字幕文本。
/// 视频按固定间隔抽帧（ffmpeg fps=1/interval），逐帧 OCR，合并相邻相同文本为句子，
/// 写入 TranscribeSentences/CurrentSentences（与转录路径同一数据槽，后续分句/清洗照常可用）。
/// </summary>
public sealed class OcrExtractOperator(
    OcrClient ocrClient,
    ILogger<OcrExtractOperator> logger) : PipelineOperatorBase<OcrExtractOperator>(logger)
{
    private static readonly HashSet<string> ImageExtensions =
        [".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp"];

    /// <summary>算子在管道中的显示名称。</summary>
    public override string Name => "OCR Extraction (GLM-OCR)";

    /// <summary>
    /// 执行 OCR 提取：抽帧（视频）或直接读图（图片），逐帧调用 GLM-OCR，
    /// 合并相邻相同文本并按帧时间生成带时间戳的句子。
    /// </summary>
    /// <param name="context">字幕工作流上下文，提供输入媒体路径与 OCR 配置。</param>
    /// <param name="cancellationToken">用于取消 OCR 过程的取消标记。</param>
    public override async Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken)
    {
        if (context.State.IsTranscribed)
        {
            LogInfo("OCR results already exist, skipping.");
            context.State.CurrentSentences = context.State.TranscribeSentences;
            return;
        }

        var config = context.Config;
        var inputPath = config.InputFilePath;
        if (string.IsNullOrEmpty(inputPath) || !File.Exists(inputPath))
            throw new FileNotFoundException($"Input file not found: {inputPath}");

        var intervalSeconds = config.OcrIntervalSeconds > 0 ? config.OcrIntervalSeconds : 2.0;
        var tempDir = context.State.PipelineTempDirectory;
        if (string.IsNullOrEmpty(tempDir))
            throw new InvalidOperationException("Pipeline temporary directory not set.");

        var isImage = ImageExtensions.Contains(Path.GetExtension(inputPath).ToLowerInvariant());
        var framePaths = isImage
            ? [inputPath]
            : await ExtractFramesAsync(inputPath, tempDir, intervalSeconds, cancellationToken);

        if (framePaths.Count == 0)
            throw new InvalidOperationException($"No frames extracted from '{inputPath}'. Is it a video or image?");

        OnProgress(5, $"OCR extracting {framePaths.Count} frames...");

        var segments = new List<OcrSegment>(framePaths.Count);
        for (var i = 0; i < framePaths.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var frameStartMs = i * intervalSeconds * 1000;
            var frameEndMs = (i + 1) * intervalSeconds * 1000;

            OnProgress(5 + 90 * i / Math.Max(1, framePaths.Count), $"Frame {i + 1}/{framePaths.Count}...");

            var raw = await ocrClient.OcrImageAsync(
                framePaths[i], ParseBackend(config.OcrBackend), config.OcrModel,
                config.OcrApiKey, config.OcrBaseUrl, cancellationToken);

            var text = NormalizeText(raw);
            if (string.IsNullOrEmpty(text))
                continue;

            segments.Add(new OcrSegment(text, frameStartMs, frameEndMs));
        }

        var sentences = MergeSegments(segments, config.Language);
        context.State.TranscribeSentences = sentences;
        context.State.CurrentSentences = sentences;
        context.State.IsTranscribed = true;

        OnProgress(100, $"OCR completed: {sentences.Count} subtitle sentences.");
        LogInfo($"OCR extracted {sentences.Count} sentences from {framePaths.Count} frames.");
    }

    /// <summary>用 ffmpeg 按 fps=1/interval 抽帧到临时目录（frame_%04d.jpg）。</summary>
    private async Task<List<string>> ExtractFramesAsync(
        string inputPath, string tempDir, double intervalSeconds, CancellationToken cancellationToken)
    {
        var frameDir = Path.Combine(tempDir, "frames");
        Directory.CreateDirectory(frameDir);
        var pattern = Path.Combine(frameDir, "frame_%04d.jpg");

        OnProgress(1, "Extracting frames with ffmpeg...");
        await FFMpegArguments
            .FromFileInput(inputPath)
            .OutputToFile(pattern, false, options => options
                .WithCustomArgument($"-vf fps=1/{intervalSeconds:0.###}")
                .WithCustomArgument("-q:v 2")
                .ForceFormat("image2"))
            .ProcessAsynchronously();

        return Directory.EnumerateFiles(frameDir, "frame_*.jpg")
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>把配置中的后端字符串解析为枚举（未知值回退智谱云端）。</summary>
    /// <param name="value">后端名：zhipu / ollama / llamacpp。</param>
    /// <returns>对应的后端枚举。</returns>
    public static OcrBackend ParseBackend(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "ollama" => OcrBackend.Ollama,
        "llamacpp" or "llama-cpp" => OcrBackend.LlamaCpp,
        _ => OcrBackend.Zhipu
    };

    /// <summary>归一化 OCR 文本：去 [NO_TEXT]、去空行、清理常见 OCR 噪声。</summary>
    internal static string NormalizeText(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var lines = raw
            .Replace("[NO_TEXT]", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();

        return string.Join("\n", lines);
    }

    /// <summary>合并相邻相同文本片段为句子（时间取首帧起、末帧止）。</summary>
    internal static List<Sentence> MergeSegments(List<OcrSegment> segments, string? language)
    {
        var sentences = new List<Sentence>();
        OcrSegment? current = null;

        foreach (var segment in segments)
        {
            if (current is null || !string.Equals(current.Text, segment.Text, StringComparison.Ordinal))
            {
                if (current is not null)
                    sentences.Add(ToSentence(current, language));
                current = segment;
            }
            else
            {
                current = current with { EndMs = segment.EndMs };
            }
        }

        if (current is not null)
            sentences.Add(ToSentence(current, language));

        return sentences;
    }

    /// <summary>片段 → 句子（文本按语言切词）。</summary>
    internal static Sentence ToSentence(OcrSegment segment, string? language)
    {
        var text = segment.Text.Replace("\n", " ", StringComparison.Ordinal).Trim();
        return new Sentence
        {
            Text = text,
            Start = segment.StartMs,
            End = segment.EndMs,
            Words = SubtitleWordSplitter.SplitPlainWords(text, segment.StartMs, segment.EndMs, language)
        };
    }

    /// <summary>OCR 片段：帧窗口内的字幕文本与时间（毫秒）。</summary>
    /// <param name="Text">字幕文本（可能多行）。</param>
    /// <param name="StartMs">起始时间（毫秒）。</param>
    /// <param name="EndMs">结束时间（毫秒）。</param>
    internal sealed record OcrSegment(string Text, double StartMs, double EndMs);
}
