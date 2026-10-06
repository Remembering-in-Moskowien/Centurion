using System.Text.Json;
using System.Text.Json.Serialization;
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

namespace Centurion.Core.Workflow.Strategy.Diarization;

/// <summary>
/// Speaker diarization backed by <c>polyvoice</c> (ekhodzitsky/polyvoice) — a CPU-only Rust
/// diarization CLI (powerset neural segmentation + WeSpeaker ResNet34 embeddings + AHC/VBx
/// clustering, no ONNX Runtime). CLI shape:
///   polyvoice diarize &lt;wav&gt; --clusterer ahc --format json --output &lt;out.json&gt; --models-cache &lt;dir&gt;
/// The balanced model pair (~8 MB) is downloaded once from the models-int8-v2 GitHub release
/// through the Downloader mirror chain.
/// </summary>
public sealed class PolyVoiceDiarizationStrategy : IDiarizationStrategy
{
    private readonly IToolManagerFactory _toolManagerFactory;
    private readonly ProcessManager _processManager;
    private readonly ILogger<PolyVoiceDiarizationStrategy> _logger;
    private readonly IServiceProvider _serviceProvider;

    /// <summary>Base URL of the balanced INT8 model pair (powerset + ResNet34), verified 2026-10.</summary>
    internal const string ModelsBaseUrl = "https://github.com/ekhodzitsky/polyvoice/releases/download/models-int8-v2";

    /// <summary>Model files required by the balanced profile, in the order the CLI expects them in the cache.</summary>
    internal static readonly string[] ModelFileNames = ["powerset_int8.onnx", "resnet34_int8.onnx"];

    /// <summary>Resolves the tool factory, process manager, and logger from the DI container.</summary>
    public PolyVoiceDiarizationStrategy(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _toolManagerFactory = serviceProvider.GetRequiredService<IToolManagerFactory>();
        _processManager = serviceProvider.GetRequiredService<ProcessManager>();
        _logger = serviceProvider.GetRequiredService<ILogger<PolyVoiceDiarizationStrategy>>();
    }
    /// <inheritdoc />
    public string StrategyName => "polyvoice";
    /// <inheritdoc />
    public StrategyCapabilities Capabilities => StrategyCapabilities.None;
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
        var toolManager = _toolManagerFactory.Create("polyvoice", device);
        await toolManager.EnsureToolAsync(cancellationToken);

        // 2. Models (auto-download through the mirror chain, content-addressed)
        var modelsDir = await EnsureModelsAsync(cancellationToken);

        // 3. 16 kHz mono input (polyvoice does no resampling)
        var wav16k = await DiarizationAudioPreprocessor.Ensure16KHzMonoAsync(
            audioPath,
            _serviceProvider.GetService<Centurion.Abstractions.IBinaryLocator>(),
            _processManager,
            cancellationToken);

        // 4. Run (machine JSON output; progress goes to stderr)
        var outJson = Path.Combine(Path.GetTempPath(), $"polyvoice-{Guid.NewGuid():N}.json");
        try
        {
            var args = BuildArguments(wav16k, modelsDir, outJson, numSpeakers);
            _logger.LogDebug("Executing polyvoice diarization: {Exe} {Args}", toolManager.ExecutablePath, string.Join(' ', args));
            await _processManager.ExecuteAsync(toolManager.ExecutablePath, args, cancellationToken);

            if (!File.Exists(outJson))
                throw new DiarizationException($"PolyVoice output JSON not found at: {outJson}");
            var json = await File.ReadAllTextAsync(outJson, cancellationToken);
            return ParseJson(json);
        }
        finally
        {
            if (File.Exists(outJson))
            {
                try { File.Delete(outJson); } catch { /* best effort */ }
            }
        }
    }

    /// <summary>
    /// Builds the polyvoice CLI argument list (internal, for unit testing).
    /// Uses the AHC clusterer (fixed-threshold cosine) so the VBx PLDA parameters — published under
    /// a separate CC-BY-4.0 bundle that is not mirrored — are not required; `--speakers` caps clustering.
    /// </summary>
    internal static IReadOnlyList<string> BuildArguments(
        string wavPath, string modelsDir, string outputJsonPath, int numSpeakers)
    {
        var args = new List<string>
        {
            "diarize", wavPath,
            "--clusterer", "ahc",
            "--format", "json",
            "--output", outputJsonPath,
            "--models-cache", modelsDir,
            "--quiet"
        };
        if (numSpeakers > 0)
        {
            args.Add("--speakers");
            args.Add(numSpeakers.ToString());
        }
        return args;
    }

    /// <summary>
    /// Ensures the two INT8 models are installed (mirror chain handles GitHub) and returns the
    /// content-addressed model cache directory (<c>models/polyvoice/&lt;aggregate-sha256&gt;/</c>).
    /// The polyvoice CLI loads its models by fixed name from the cache directory, so member files
    /// keep their original names inside the hash-named directory.
    /// </summary>
    internal async Task<string> EnsureModelsAsync(CancellationToken cancellationToken)
    {
        var meta = new Centurion.Models.Metadata.ModelMeta(
            ModelsBaseUrl, ModelFileNames.ToList());
        var dict = new Dictionary<string, Centurion.Models.Metadata.ModelMeta>(StringComparer.OrdinalIgnoreCase)
        {
            ["models"] = meta
        };
        using var manager = new Centurion.Core.Capabilities.Managers.Media.ModelManager(
            "models", dict, _serviceProvider, "polyvoice");
        await manager.EnsureInstalledAsync(cancellationToken);
        if (string.IsNullOrEmpty(manager.ModelFolder))
            throw new DiarizationException("PolyVoice model download failed (no directory resolved).");
        return manager.ModelFolder;
    }

    /// <summary>Parses the polyvoice machine JSON (<c>segments[]</c> with time.start/end + speaker) into speaker segments.</summary>
    internal static IReadOnlyList<SpeakerSegment> ParseJson(string json)
    {
        var result = JsonSerializer.Deserialize<PolyVoiceResult>(json, JsonOptions);
        if (result?.Segments is null || result.Segments.Count == 0)
            return [];

        return result.Segments
            .Where(s => s.Time is not null)
            .Select(s => new SpeakerSegment(
                s.Time!.Start,
                Math.Max(s.Time.End, s.Time.Start),
                $"SPEAKER_{s.Speaker:00}"))
            .ToList();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    private sealed class PolyVoiceResult
    {
        [JsonPropertyName("segments")] public List<PolyVoiceSegment>? Segments { get; set; }
    }

    private sealed class PolyVoiceSegment
    {
        [JsonPropertyName("time")] public PolyVoiceTime? Time { get; set; }
        [JsonPropertyName("speaker")] public int Speaker { get; set; }
    }

    private sealed class PolyVoiceTime
    {
        [JsonPropertyName("start")] public double Start { get; set; }
        [JsonPropertyName("end")] public double End { get; set; }
    }
}
