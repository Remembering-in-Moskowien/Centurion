using System.Text.RegularExpressions;
using Centurion.Abstractions;
using Centurion.Abstractions.Strategy;
using Centurion.Abstractions.Exceptions;
using Centurion.Core.Capabilities.Managers.Runtime;
using Centurion.Core.Capabilities.Managers.Tools;
using Centurion.Core.Operators.Download;
using Centurion.Core.Operators.Download.Request;
using Centurion.Core.Workflow.Factories;
using Centurion.Models.Workflow;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpCompress.Archives;

namespace Centurion.Core.Workflow.Strategy.Diarization;

/// <summary>
/// Speaker diarization backed by the official sherpa-onnx C++ CLI
/// (<c>sherpa-onnx-offline-speaker-diarization</c>) — the standard "WeSpeaker cpp" deployment:
/// pyannote segmentation ONNX + WeSpeaker ResNet34 embedding ONNX + fast clustering.
/// CLI shape:
///   sherpa-onnx-offline-speaker-diarization --segmentation.pyannote-model=&lt;seg.onnx&gt; \
///       --embedding.model=&lt;embedder.onnx&gt; [--clustering.num-clusters=N] &lt;wav16k&gt;
/// Output is printed to stdout as <c>start -- end speaker_NN</c> lines.
/// </summary>
public sealed class WeSpeakerDiarizationStrategy : IDiarizationStrategy
{
    private readonly IToolManagerFactory _toolManagerFactory;
    private readonly ProcessManager _processManager;
    private readonly ILogger<WeSpeakerDiarizationStrategy> _logger;
    private readonly IServiceProvider _serviceProvider;

    /// <summary>pyannote segmentation-3.0 package (tar.bz2, GitHub release).</summary>
    internal const string SegmentationPackageUrl = "https://github.com/k2-fsa/sherpa-onnx/releases/download/speaker-segmentation-models/sherpa-onnx-pyannote-segmentation-3-0.tar.bz2";

    /// <summary>WeSpeaker ResNet34 (zh, CN-Celeb) embedding model — single ONNX file.</summary>
    internal const string EmbedderUrl = "https://github.com/k2-fsa/sherpa-onnx/releases/download/speaker-recongition-models/wespeaker_zh_cnceleb_resnet34_LM.onnx";

    /// <summary>Filename of the segmentation ONNX inside the package.</summary>
    internal const string SegmentationOnnxFileName = "model.onnx";

    private static readonly Regex TurnLineRegex = new(
        @"^\s*(?:Started\s+)?([0-9]+(?:\.[0-9]+)?)\s*--\s*([0-9]+(?:\.[0-9]+)?)\s+(speaker_\d+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Resolves the tool factory, process manager, and logger from the DI container.</summary>
    public WeSpeakerDiarizationStrategy(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _toolManagerFactory = serviceProvider.GetRequiredService<IToolManagerFactory>();
        _processManager = serviceProvider.GetRequiredService<ProcessManager>();
        _logger = serviceProvider.GetRequiredService<ILogger<WeSpeakerDiarizationStrategy>>();
    }

    /// <inheritdoc />
    public string StrategyName => "wespeaker";

    /// <inheritdoc />
    public async Task<IReadOnlyList<SpeakerSegment>> DiarizeAsync(
        string audioPath,
        int numSpeakers,
        string? segmentModel,
        CancellationToken cancellationToken = default,
        InferenceDevice device = InferenceDevice.Auto)
    {
        if (!File.Exists(audioPath))
            throw new FileNotFoundException($"Audio file not found: {audioPath}");

        // 1. Tool
        var toolManager = _toolManagerFactory.Create("sherpa-onnx", device);
        await toolManager.EnsureToolAsync(cancellationToken);

        // 2. Models (auto-download through the mirror chain)
        var modelsDir = Path.Combine(AppContext.BaseDirectory, "models", "sherpa-diarization");
        var (segmentationPath, embedderPath) = await EnsureModelsAsync(modelsDir, cancellationToken);

        // 3. 16 kHz mono input (sherpa-onnx expects 16k mono WAV)
        var wav16k = await DiarizationAudioPreprocessor.Ensure16KHzMonoAsync(
            audioPath,
            _serviceProvider.GetService<Centurion.Abstractions.IBinaryLocator>(),
            _processManager,
            cancellationToken);

        // 4. Run and parse stdout
        var args = BuildArguments(wav16k, segmentationPath, embedderPath, numSpeakers);
        _logger.LogDebug("Executing sherpa-onnx diarization: {Exe} {Args}", toolManager.ExecutablePath, string.Join(' ', args));
        var stdout = await _processManager.ExecuteAsync(toolManager.ExecutablePath, args, cancellationToken);
        return ParseStdout(stdout);
    }

    /// <summary>
    /// Builds the sherpa-onnx offline-speaker-diarization arguments (internal, for unit testing).
    /// The CLI parses options strictly as <c>--x=y</c> pairs. A known speaker count maps to
    /// <c>--clustering.num-clusters</c>; unknown counts use the default threshold-based clustering.
    /// </summary>
    internal static IReadOnlyList<string> BuildArguments(
        string wavPath, string segmentationOnnx, string embedderOnnx, int numSpeakers)
    {
        var args = new List<string>
        {
            $"--segmentation.pyannote-model={segmentationOnnx}",
            $"--embedding.model={embedderOnnx}"
        };
        if (numSpeakers > 0)
        {
            args.Add($"--clustering.num-clusters={numSpeakers}");
        }
        args.Add(wavPath);
        return args;
    }

    /// <summary>Downloads/extracts the pyannote segmentation package and the WeSpeaker ONNX embedder.</summary>
    internal async Task<(string SegmentationPath, string EmbedderPath)> EnsureModelsAsync(
        string modelsDir, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(modelsDir);
        using var downloader = _serviceProvider.GetRequiredService<Centurion.Core.Operators.Download.Downloader>();

        // Segmentation: tar.bz2 package -> extract, locate model.onnx (the package nests it in a subdirectory).
        var packagePath = Path.Combine(modelsDir, "pyannote-segmentation-3-0.tar.bz2");
        var segDir = Path.Combine(modelsDir, "pyannote-segmentation-3-0");
        string? FindSegmentation() => Directory.Exists(segDir)
            ? Directory.EnumerateFiles(segDir, SegmentationOnnxFileName, SearchOption.AllDirectories).FirstOrDefault()
            : null;
        var segmentationPath = FindSegmentation();
        if (segmentationPath is null)
        {
            if (!File.Exists(packagePath) || new FileInfo(packagePath).Length == 0)
            {
                _logger.LogInformation("Downloading pyannote segmentation model ...");
                await downloader.ProcessAsync(new OperatorsRequest<AriaDownloadRequest>
                {
                    Payload = new AriaDownloadRequest
                    {
                        Url = SegmentationPackageUrl,
                        FullSavePath = packagePath,
                        MaxRetry = 3,
                        ProgressRefreshMs = 100
                    }
                }, cancellationToken);
            }

            Directory.CreateDirectory(segDir);
            using (var stream = File.OpenRead(packagePath))
            using (var bz2 = SharpCompress.Compressors.BZip2.BZip2Stream.Create(
                stream, SharpCompress.Compressors.CompressionMode.Decompress, false))
            using (var reader = SharpCompress.Readers.ReaderFactory.OpenReader(bz2))
            {
                var root = Path.GetFullPath(segDir);
                while (reader.MoveToNextEntry())
                {
                    if (reader.Entry.IsDirectory || reader.Entry.Key == null)
                        continue;
                    var fullPath = Path.GetFullPath(Path.Combine(root, reader.Entry.Key));
                    if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                        throw new InvalidDataException($"Unsafe archive entry path rejected: {reader.Entry.Key}");
                    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                    using var entryStream = reader.OpenEntryStream();
                    using var fileStream = File.Create(fullPath);
                    await entryStream.CopyToAsync(fileStream, cancellationToken);
                }
            }
            File.Delete(packagePath);
            segmentationPath = FindSegmentation()
                ?? throw new DiarizationException("pyannote segmentation model.onnx not found after extraction.");
        }

        // Embedder: single ONNX file.
        var embedderPath = Path.Combine(modelsDir, "wespeaker_zh_cnceleb_resnet34_LM.onnx");
        if (!File.Exists(embedderPath) || new FileInfo(embedderPath).Length == 0)
        {
            _logger.LogInformation("Downloading WeSpeaker embedder model ...");
            await downloader.ProcessAsync(new OperatorsRequest<AriaDownloadRequest>
            {
                Payload = new AriaDownloadRequest
                {
                    Url = EmbedderUrl,
                    FullSavePath = embedderPath,
                    MaxRetry = 3,
                    ProgressRefreshMs = 100
                }
            }, cancellationToken);
        }

        return (segmentationPath ?? throw new DiarizationException("pyannote segmentation model.onnx not found."), embedderPath);
    }

    /// <summary>Parses the stdout turn lines <c>start -- end speaker_NN</c> into speaker segments.</summary>
    internal static IReadOnlyList<SpeakerSegment> ParseStdout(string stdout)
    {
        var segments = new List<SpeakerSegment>();
        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = TurnLineRegex.Match(line);
            if (!match.Success)
                continue;
            var start = double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            var end = double.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            // Normalize "speaker_00" to the pipeline's canonical "SPEAKER_00" label.
            segments.Add(new SpeakerSegment(start, Math.Max(end, start), match.Groups[3].Value.ToUpperInvariant()));
        }
        return segments;
    }
}
