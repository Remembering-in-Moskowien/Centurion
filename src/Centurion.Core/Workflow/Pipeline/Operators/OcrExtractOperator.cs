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
/// OCR extraction operator (the ocr command): uses GLM-OCR to extract subtitle text from video frames or images.
/// Videos are sampled at a fixed interval (ffmpeg fps=1/interval), OCR runs per frame, adjacent identical text is merged into sentences,
/// and the result is written to TranscribeSentences/CurrentSentences (the same slots as the transcription path, so downstream sentence splitting / cleaning keep working).
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

    /// <summary>Display name of the operator in the pipeline.</summary>
    public override string Name => "OCR Extraction (GLM-OCR)";

    /// <summary>
    /// Runs OCR extraction: extracts frames (video) or reads the image directly, calls GLM-OCR per frame,
    /// merges adjacent identical text and produces timestamped sentences from frame times.
    /// </summary>
    /// <param name="context">Subtitle workflow context, providing the input media path and OCR configuration.</param>
    /// <param name="cancellationToken">Cancellation token used to cancel the OCR process.</param>
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
                    config, inputPath, tempDir, videoSubFinderPath, cancellationToken);
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
        WorkflowConfig config, string inputPath, string tempDir, string executablePath, CancellationToken cancellationToken)
    {
        var outputDir = Path.Combine(tempDir, "videosubfinder");
        Directory.CreateDirectory(outputDir);
        var timecodesPath = Path.Combine(outputDir, "timings.srt");

        try
        {
            OnProgress(1, "Detecting subtitle frames with VideoSubFinder...");
            // VSF WXW (a wxWidgets GUI program) returns exit code -1 even on success,
            // so ignore the exit code and judge success only by the outputs (RGBImages + SRT).
            var vsfArgs = BuildVideoSubFinderArguments(config, inputPath, timecodesPath, outputDir);
            await processManager.ExecuteAsync(executablePath, vsfArgs, cancellationToken, throwOnNonZeroExit: false);

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
            // VideoSubFinder is an enhancement path: any failure (process error / missing output /
            // timing mismatch) falls back to fixed-interval extraction, so the main OCR pipeline is never blocked by this helper tool.
            LogWarning($"VideoSubFinder subtitle-frame detection failed ({ex.Message}); will use fixed-interval extraction.");
            return [];
        }
    }

    /// <summary>
    /// Builds VideoSubFinder command-line arguments (subtitle detection region comes from config, falling back to the default subtitle area when unset).
    /// </summary>
    /// <param name="config">Workflow configuration (the four OCR ROI edge ratios, may be null).</param>
    /// <param name="inputPath">Input video path.</param>
    /// <param name="timecodesPath">Timecode SRT output path (--create_empty_sub).</param>
    /// <param name="outputDir">Output directory.</param>
    internal static IReadOnlyList<string> BuildVideoSubFinderArguments(
        WorkflowConfig config, string inputPath, string timecodesPath, string outputDir) =>
    [
        "-c", // clear dirs
        "-r", // run search
        "--create_empty_sub", timecodesPath,
        "-i", inputPath,
        "-o", outputDir,
        "-te", FormatRoi(config.OcrRoiTop, 0.2102),
        "-be", FormatRoi(config.OcrRoiBottom, 0.0),
        "-le", FormatRoi(config.OcrRoiLeft, 0.0),
        "-re", FormatRoi(config.OcrRoiRight, 1.0)
    ];

    /// <summary>Formats an ROI ratio value as a VSF argument (fallback when unset, invariant culture, fixed decimals).</summary>
    internal static string FormatRoi(double? value, double fallback) =>
        (value ?? fallback).ToString("0.####", CultureInfo.InvariantCulture);

    private static List<OcrFrame> CreateIntervalFrames(IReadOnlyList<string> framePaths, double intervalSeconds) =>
        framePaths.Select((path, index) => new OcrFrame(
            path, index * intervalSeconds * 1000, (index + 1) * intervalSeconds * 1000)).ToList();

    /// <summary>
    /// Pairs VideoSubFinder's subtitle frame images with their timecodes.
    /// The image filenames embed start/end times (VSF format: <c>h_mm_ss_mmm__h_mm_ss_mmm_coords.jpg</c>),
    /// so filenames are used as the time source for ordering, then cross-checked against the SRT generated by
    /// <c>--create_empty_sub</c>: counts must match and each frame's start time must align with the SRT (within <see cref="VsTimeToleranceMs"/>), otherwise the output is treated as abnormal.
    /// </summary>
    internal static List<OcrFrame> CreateVideoSubFinderFrames(IEnumerable<string> imagePaths, string timingSrt)
    {
        // 1. Parse embedded times from filenames and sort by time (not by filename string order, to avoid misalignment when the hour field's digit count changes)
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

        // 2. Parse the SRT timecodes
        var timecodes = VideoSubFinderTimecodeRegex().Matches(timingSrt);
        if (parsedImages.Count != timecodes.Count)
            throw new InvalidOperationException(
                $"VideoSubFinder output mismatch: found {parsedImages.Count} images and {timecodes.Count} time ranges.");

        // 3. Cross-check: the embedded filename start time must match the corresponding SRT entry (same source, should match exactly)
        for (var index = 0; index < parsedImages.Count; index++)
        {
            var srtStart = ParseVideoSubFinderTimecode(timecodes[index], 1);
            if (Math.Abs(parsedImages[index].StartMs - srtStart) > VsTimeToleranceMs)
                throw new InvalidOperationException(
                    $"VideoSubFinder timing mismatch at entry {index}: image start {parsedImages[index].StartMs}ms, SRT start {srtStart}ms.");
        }

        return parsedImages.Select(x => new OcrFrame(x.Path, x.StartMs, x.EndMs)).ToList();
    }

    /// <summary>Max allowed deviation (ms) between embedded filename time and SRT time, guarding against millisecond rounding differences.</summary>
    private const double VsTimeToleranceMs = 5;

    /// <summary>Parses the embedded time in a VSF image filename (group offset 1=start, 5=end; four fields h_mm_ss_mmm).</summary>
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

    /// <summary>VSF RGBImages filename: &lt;h&gt;_&lt;mm&gt;_&lt;ss&gt;_&lt;mmm&gt;__&lt;h&gt;_&lt;mm&gt;_&lt;ss&gt;_&lt;mmm&gt;_&lt;coords etc.&gt;.</summary>
    [GeneratedRegex(@"^(\d+)_(\d{2})_(\d{2})_(\d{3})__(\d+)_(\d{2})_(\d{2})_(\d{3})_")]
    private static partial Regex VsfImageNameRegex();

    [GeneratedRegex(@"(?m)^\s*(\d{2,}):(\d{2}):(\d{2})[,.](\d{3})\s*-->\s*(\d{2,}):(\d{2}):(\d{2})[,.](\d{3})")]
    private static partial Regex VideoSubFinderTimecodeRegex();

    /// <summary>Extracts frames to the temp directory with ffmpeg at fps=1/interval (frame_%04d.jpg).</summary>
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

    /// <summary>Parses the configured backend string into the enum (unknown values fall back to the Zhipu cloud).</summary>
    /// <param name="value">Backend name: zhipu / ollama / llamacpp.</param>
    /// <returns>The corresponding backend enum.</returns>
    public static OcrBackend ParseBackend(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "ollama" => OcrBackend.Ollama,
        "llamacpp" or "llama-cpp" => OcrBackend.LlamaCpp,
        "rapidocr" or "rapid-ocr" or "rapid" => OcrBackend.RapidOcr,
        _ => OcrBackend.Zhipu
    };

    /// <summary>Normalizes OCR text: drops [NO_TEXT], removes blank lines, and cleans common OCR noise.</summary>
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

    /// <summary>Merges adjacent identical-text segments into sentences (time spans from the first to the last frame).</summary>
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

    /// <summary>Segment to sentence (text is split into words per language).</summary>
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

    /// <summary>An OCR segment: subtitle text within a frame window and its timing (ms).</summary>
    /// <param name="Text">Subtitle text (may span multiple lines).</param>
    /// <param name="StartMs">Start time (ms).</param>
    /// <param name="EndMs">End time (ms).</param>
    internal sealed record OcrSegment(string Text, double StartMs, double EndMs);

    /// <summary>An image pending OCR and the detected time range (ms).</summary>
    internal sealed record OcrFrame(string Path, double StartMs, double EndMs);
}
