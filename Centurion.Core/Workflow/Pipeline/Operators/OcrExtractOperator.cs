using Centurion.Abstractions.Pipeline;
using Centurion.Models;
using Centurion.Models.Workflow;
using Centurion.Core.Capabilities.Infrastructure.Ocr;
using FFMpegCore;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.RegularExpressions;
using Centurion.Core.Capabilities.Managers.Runtime;
using Centurion.Core.Capabilities.Managers.Tools;
using Centurion.Core.Utils.Parsing;
namespace Centurion.Core.Workflow.Pipeline.Operators;

/// <summary>
/// OCR 提取算子（ocr 命令）：用 GLM-OCR 从视频帧/图片中提取字幕文本。
/// 视频按固定间隔抽帧（ffmpeg fps=1/interval），逐帧 OCR，合并相邻相同文本为句子，
/// 写入 TranscribeSentences/CurrentSentences（与转录路径同一数据槽，后续分句/清洗照常可用）。
/// </summary>
public sealed partial class OcrExtractOperator(
    OcrClient ocrClient,
    ProcessManager processManager,
    VideoSubFinderManager videoSubFinderManager,
    RapidOcrEngine? rapidOcrEngine,
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
        List<OcrFrame> frames;
        if (isImage)
        {
            frames = [new OcrFrame(inputPath, 0, intervalSeconds * 1000)];
        }
        else
        {
            var configuredPath = config.OcrVideoSubFinderPath;
            var videoSubFinderPath = LocateVideoSubFinderExecutable(configuredPath);
            if (!string.IsNullOrWhiteSpace(configuredPath) &&
                (videoSubFinderPath is null || !File.Exists(videoSubFinderPath)))
            {
                LogWarning($"Configured VideoSubFinder path does not exist: {configuredPath}; falling back to PATH lookup.");
                videoSubFinderPath = LocateVideoSubFinderExecutable(null);
            }

            if (string.IsNullOrWhiteSpace(configuredPath) && videoSubFinderPath is null)
                videoSubFinderPath = await videoSubFinderManager.EnsureInstalledAsync(cancellationToken);

            if (videoSubFinderPath is null)
            {
                LogInfo("VideoSubFinder CLI is unavailable; falling back to fixed-interval extraction.");
                var framePaths = await ExtractFramesAsync(inputPath, tempDir, intervalSeconds, cancellationToken);
                frames = CreateIntervalFrames(framePaths, intervalSeconds);
            }
            else
            {
                frames = await ExtractVideoSubFinderFramesAsync(
                    inputPath, tempDir, videoSubFinderPath, cancellationToken);
                if (frames.Count == 0)
                {
                    LogInfo("VideoSubFinder found no subtitle frames; falling back to fixed-interval extraction.");
                    var framePaths = await ExtractFramesAsync(inputPath, tempDir, intervalSeconds, cancellationToken);
                    frames = CreateIntervalFrames(framePaths, intervalSeconds);
                }
            }
        }

        if (frames.Count == 0)
            throw new InvalidOperationException($"No frames extracted from '{inputPath}'. Is it a video or image?");

        OnProgress(5, $"OCR extracting {frames.Count} frames...");

        var segments = new List<OcrSegment>(frames.Count);
        for (var i = 0; i < frames.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var frame = frames[i];

            OnProgress(5 + 90 * i / Math.Max(1, frames.Count), $"Frame {i + 1}/{frames.Count}...");

            var backend = ParseBackend(config.OcrBackend);
            var raw = backend == OcrBackend.RapidOcr
                ? await (rapidOcrEngine
                    ?? throw new InvalidOperationException("RapidOCR engine is not registered."))
                    .OcrImageAsync(frame.Path, cancellationToken)
                : await ocrClient.OcrImageAsync(
                    frame.Path, backend, config.OcrModel,
                    config.OcrApiKey, config.OcrBaseUrl, cancellationToken);

            var text = NormalizeText(raw);
            if (string.IsNullOrEmpty(text))
                continue;

            segments.Add(new OcrSegment(text, frame.StartMs, frame.EndMs));
        }

        var sentences = MergeSegments(segments, config.Language);
        context.State.TranscribeSentences = sentences;
        context.State.CurrentSentences = sentences;
        context.State.IsTranscribed = true;

        OnProgress(100, $"OCR completed: {sentences.Count} subtitle sentences.");
        LogInfo($"OCR extracted {sentences.Count} sentences from {frames.Count} frames.");
    }

    internal static string? LocateVideoSubFinderExecutable(
        string? configuredPath,
        IEnumerable<string>? searchDirectories = null,
        bool? isWindows = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
            return configuredPath;

        var windows = isWindows ?? OperatingSystem.IsWindows();
        var executableNames = windows
            ? new[] { "VideoSubFinderWXW_intel.exe", "VideoSubFinderWXW.exe", "VideoSubFinderCli.exe" }
            : new[] { "VideoSubFinderCli", "VideoSubFinderCli.run" };
        var directories = searchDirectories ??
            (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator);

        foreach (var directory in directories)
        {
            var normalizedDirectory = directory.Trim().Trim('"');
            if (normalizedDirectory.Length == 0)
                continue;

            foreach (var executableName in executableNames)
            {
                var candidate = Path.Combine(normalizedDirectory, executableName);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    private async Task<List<OcrFrame>> ExtractVideoSubFinderFramesAsync(
        string inputPath, string tempDir, string executablePath, CancellationToken cancellationToken)
    {
        var outputDir = Path.Combine(tempDir, "videosubfinder");
        Directory.CreateDirectory(outputDir);
        var timecodesPath = Path.Combine(outputDir, "timings.srt");

        try
        {
            OnProgress(1, "Detecting subtitle frames with VideoSubFinder...");
            // VSF WXW（wxWidgets GUI 程序）即使正常完成也返回退出码 -1，
            // 因此忽略退出码，仅以输出产物（RGBImages + SRT）判定成功。
            await processManager.ExecuteAsync(executablePath,
            [
                "-c", // clear dirs
                "-r", // run search
                "--create_empty_sub", timecodesPath,
                "-i", inputPath,
                "-o", outputDir,
                "-te", "0.2102", // 字幕区顶部（视频高度比例，VSF 6.10 实测有效）
                "-be", "0",
                "-le", "0",
                "-re", "1"
            ], cancellationToken, throwOnNonZeroExit: false);

            var imageDir = Path.Combine(outputDir, "RGBImages");
            if (!Directory.Exists(imageDir))
                throw new InvalidOperationException("VideoSubFinder did not produce an RGBImages output directory.");

            var imagePaths = Directory.EnumerateFiles(imageDir)
                .Where(path => ImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()))
                .ToList();
            if (imagePaths.Count == 0)
                return [];
            if (!File.Exists(timecodesPath))
                throw new InvalidOperationException("VideoSubFinder produced subtitle images but no timing SRT output.");

            return CreateVideoSubFinderFrames(imagePaths, await File.ReadAllTextAsync(timecodesPath, cancellationToken));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // VideoSubFinder 是增强路径：任何失败（进程错误/输出缺失/时间配对不一致）
            // 都降级为固定间隔抽帧，不让 OCR 主链路被辅助工具阻断。
            LogWarning($"VideoSubFinder subtitle-frame detection failed ({ex.Message}); will use fixed-interval extraction.");
            return [];
        }
    }

    private static List<OcrFrame> CreateIntervalFrames(IReadOnlyList<string> framePaths, double intervalSeconds) =>
        framePaths.Select((path, index) => new OcrFrame(
            path, index * intervalSeconds * 1000, (index + 1) * intervalSeconds * 1000)).ToList();

    /// <summary>
    /// 把 VideoSubFinder 输出的字幕帧图片与时间码配对。
    /// 图片文件名内嵌开始/结束时间（VSF 格式：<c>h_mm_ss_mmm__h_mm_ss_mmm_坐标.jpg</c>），
    /// 因此以文件名为时间源排序，再与 <c>--create_empty_sub</c> 生成的 SRT 交叉校验：
    /// 数量一致且各帧开始时间与 SRT 对齐（容差 <see cref="VsTimeToleranceMs"/>），否则视为输出异常。
    /// </summary>
    internal static List<OcrFrame> CreateVideoSubFinderFrames(IEnumerable<string> imagePaths, string timingSrt)
    {
        // 1. 从文件名解析内嵌时间并按时间排序（不依赖文件名字符串顺序，避免小时字段位数变化导致错位）
        var parsedImages = new List<(string Path, double StartMs, double EndMs)>();
        foreach (var path in imagePaths)
        {
            var fileName = Path.GetFileName(path);
            var match = VsfImageNameRegex().Match(fileName);
            if (!match.Success)
                throw new InvalidOperationException(
                    $"Unrecognized VideoSubFinder image name '{fileName}': expected '<h>_<mm>_<ss>_<mmm>__<h>_<mm>_<ss>_<mmm>_...'.");
            parsedImages.Add((
                path,
                ParseVideoSubFinderImageTime(match, 1),
                ParseVideoSubFinderImageTime(match, 5)));
        }

        parsedImages.Sort(static (a, b) =>
        {
            var cmp = a.StartMs.CompareTo(b.StartMs);
            return cmp != 0 ? cmp : a.EndMs.CompareTo(b.EndMs);
        });

        // 2. 解析 SRT 时间码
        var timecodes = VideoSubFinderTimecodeRegex().Matches(timingSrt);
        if (parsedImages.Count != timecodes.Count)
            throw new InvalidOperationException(
                $"VideoSubFinder output mismatch: found {parsedImages.Count} images and {timecodes.Count} time ranges.");

        // 3. 交叉校验：文件名内嵌开始时间必须与 SRT 对应段一致（同源生成，应完全匹配）
        for (var index = 0; index < parsedImages.Count; index++)
        {
            var srtStart = ParseVideoSubFinderTimecode(timecodes[index], 1);
            if (Math.Abs(parsedImages[index].StartMs - srtStart) > VsTimeToleranceMs)
                throw new InvalidOperationException(
                    $"VideoSubFinder timing mismatch at entry {index}: image start {parsedImages[index].StartMs}ms, SRT start {srtStart}ms.");
        }

        return parsedImages.Select(x => new OcrFrame(x.Path, x.StartMs, x.EndMs)).ToList();
    }

    /// <summary>文件名内嵌时间与 SRT 时间允许的最大偏差（毫秒，防毫秒舍入差异）。</summary>
    private const double VsTimeToleranceMs = 5;

    /// <summary>解析 VSF 图片文件名内嵌时间（组偏移 1=开始，5=结束；h_mm_ss_mmm 四位字段）。</summary>
    private static double ParseVideoSubFinderImageTime(Match match, int groupOffset) =>
        ParseTimeComponents(match.Groups[groupOffset].Value,
            match.Groups[groupOffset + 1].Value,
            match.Groups[groupOffset + 2].Value,
            match.Groups[groupOffset + 3].Value);

    private static double ParseVideoSubFinderTimecode(Match match, int groupOffset) =>
        ParseTimeComponents(match.Groups[groupOffset].Value,
            match.Groups[groupOffset + 1].Value,
            match.Groups[groupOffset + 2].Value,
            match.Groups[groupOffset + 3].Value);

    private static double ParseTimeComponents(string hours, string minutes, string seconds, string milliseconds) =>
        (((long.Parse(hours, CultureInfo.InvariantCulture) * 60 +
           long.Parse(minutes, CultureInfo.InvariantCulture)) * 60 +
           long.Parse(seconds, CultureInfo.InvariantCulture)) * 1000) +
        long.Parse(milliseconds, CultureInfo.InvariantCulture);

    /// <summary>VSF RGBImages 文件名：&lt;h&gt;_&lt;mm&gt;_&lt;ss&gt;_&lt;mmm&gt;__&lt;h&gt;_&lt;mm&gt;_&lt;ss&gt;_&lt;mmm&gt;_&lt;坐标等&gt;。</summary>
    [GeneratedRegex(@"^(\d+)_(\d{2})_(\d{2})_(\d{3})__(\d+)_(\d{2})_(\d{2})_(\d{3})_")]
    private static partial Regex VsfImageNameRegex();

    [GeneratedRegex(@"(?m)^\s*(\d{2,}):(\d{2}):(\d{2})[,.](\d{3})\s*-->\s*(\d{2,}):(\d{2}):(\d{2})[,.](\d{3})")]
    private static partial Regex VideoSubFinderTimecodeRegex();

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
        "rapidocr" or "rapid-ocr" or "rapid" => OcrBackend.RapidOcr,
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

    /// <summary>待 OCR 图像及检测到的时间范围（毫秒）。</summary>
    internal sealed record OcrFrame(string Path, double StartMs, double EndMs);
}
