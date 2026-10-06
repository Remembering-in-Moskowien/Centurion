using Centurion.Core.Workflow.Factories;using Centurion.Models.Workflow;

using Centurion.Abstractions;
using Centurion.Abstractions.Factories;
using Centurion.Abstractions.Strategy;
using Centurion.Abstractions.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Centurion.Core.Capabilities.Managers.Runtime;
using Centurion.Core.Capabilities.Managers.Tools;
using Centurion.Core.Utils.Parsing;
namespace Centurion.Core.Workflow.Strategy.Diarization;

/// <summary>
/// Base class for CrispASR speaker diarization strategies.
/// The two schemes (CrispASR built-in methods / Pyannote + TitaNet) share the same CLI invocation and
/// JSON parsing flow, differing only in command-line arguments (method, embedder, segmentation model).
/// </summary>
public abstract class CrispAsrDiarizationBase : IDiarizationStrategy
{
    /// <summary>Factory that creates the CrispASR tool manager per device.</summary>
    protected readonly IToolManagerFactory _toolManagerFactory;
    /// <summary>Executor that starts and manages external CLI processes.</summary>
    protected readonly ProcessManager _processManager;
    /// <summary>Resolver for the local paths of model files.</summary>
    protected readonly IModelPathResolver _modelResolver;
    /// <summary>Logger for recording the diarization process.</summary>
    protected readonly ILogger _logger;
    private readonly IServiceProvider _serviceProvider;
    private ToolManager? _toolManager;

    /// <summary>hf-mirror URL of the WeSpeaker embedder required by the foxnose method (crispasr auto-downloads it from huggingface.co, which is unreachable from CN networks).</summary>
    private const string WespeakerEmbedderUrl = "https://hf-mirror.com/cstr/wespeaker-resnet34-lm-GGUF/resolve/main/wespeaker-resnet34-lm.gguf";

    /// <summary>Method name passed to --diarize-method (energy/xcorr/vad-turns/foxnose/pyannote).</summary>
    protected abstract string DiarizeMethod { get; }

    /// <summary>Embedder passed to --diarize-embedder ("auto" = TitaNet; null = not passed).</summary>
    protected virtual string? DiarizeEmbedder => null;

    /// <summary>Segmentation model name required by the pyannote method (e.g. "pyannote-seg-3.0", auto-downloaded by CrispASR).</summary>
    protected virtual string? DefaultSegmentModel => null;

    /// <summary>Resolves the required services from the dependency injection container and initializes the shared base dependencies.</summary>
    /// <param name="serviceProvider">Container used to resolve the tool factory, process manager, model resolver, and logger.</param>
    protected CrispAsrDiarizationBase(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _toolManagerFactory = serviceProvider.GetRequiredService<IToolManagerFactory>();
        _processManager = serviceProvider.GetRequiredService<ProcessManager>();
        _modelResolver = serviceProvider.GetRequiredService<IModelPathResolver>();
        _logger = serviceProvider.GetRequiredService<ILogger<CrispAsrDiarizationBase>>();
    }

    /// <summary>Creates (lazily) the CrispASR tool manager for the given inference device.</summary>
    protected ToolManager GetToolManager(InferenceDevice device) =>
        _toolManager ??= _toolManagerFactory.Create("crispasr", device);

    /// <summary>Display name of the strategy.</summary>
    public abstract string StrategyName { get; }

    /// <summary>
    /// Performs speaker diarization on the audio: ensures CrispASR is ready, resolves the model, builds and
    /// runs the CLI, then parses the output JSON into a list of speaker segments.
    /// </summary>
    /// <param name="audioPath">Path to the audio file to diarize.</param>
    /// <param name="numSpeakers">Expected number of speakers; when greater than 0 it is passed as the max-speaker limit.</param>
    /// <param name="segmentModel">Segmentation model name; falls back to the implementation's default model when null.</param>
    /// <param name="cancellationToken">Token used to cancel the diarization process.</param>
    /// <param name="device">Inference device, which selects the CPU/GPU tool variant.</param>
    public virtual async Task<IReadOnlyList<SpeakerSegment>> DiarizeAsync(
        string audioPath,
        int numSpeakers,
        string? segmentModel,
        CancellationToken cancellationToken = default,
        InferenceDevice device = InferenceDevice.Auto)
    {
        if (!File.Exists(audioPath))
            throw new FileNotFoundException($"Audio file not found: {audioPath}");

        // 1. Ensure the CrispASR tool is ready (the GPU variant is auto-selected by device)
        var toolManager = GetToolManager(device);
        await toolManager.EnsureToolAsync(cancellationToken);

        // 2. Diarization uses the same qwen3 model as the main transcription (whisper tiny gives unstable
        //    segment boundaries that drift between runs; the same model/backend is more deterministic)
        var modelPath = await _modelResolver.GetQwen3AsrModelPathAsync("qwen3-asr-1.7b", cancellationToken);
        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"Qwen3 ASR model not found: {modelPath}");

        // 3. Build arguments and run (--diarize-speakers writes speaker labels into the speaker field of each transcription entry)
        var jsonBasePath = Path.Combine(
            Path.GetDirectoryName(audioPath) ?? string.Empty,
            Path.GetFileNameWithoutExtension(audioPath) + "_diar");
        var embedder = await EnsureEmbedderAsync(cancellationToken) ?? DiarizeEmbedder;
        var args = BuildArguments(audioPath, modelPath, jsonBasePath, numSpeakers, segmentModel, DiarizeMethod, embedder, DefaultSegmentModel, backend: "qwen3");

        _logger.LogDebug("Executing diarization: {Exe} {Args}", toolManager.ExecutablePath, args);
        await _processManager.ExecuteAsync(toolManager.ExecutablePath, args, cancellationToken);

        // 4. Read and parse the output
        var jsonPath = jsonBasePath + ".json";
        if (!File.Exists(jsonPath))
            throw new DiarizationException($"Diarization output JSON not found at: {jsonPath}");

        var json = await File.ReadAllTextAsync(jsonPath, cancellationToken);
        return DiarizationJsonParser.Parse(json);
    }

    /// <summary>
    /// Builds the CrispASR diarization command-line arguments (internal, for unit testing).
    /// Uses --diarize-speakers so that speaker labels are written into the speaker field of each transcription entry;
    /// --diarize-method may still be foxnose (WeSpeaker embeddings) or pyannote (TitaNet embeddings).
    /// The segmentation model (e.g. pyannote-seg-3.0.gguf) is auto-downloaded and cached by CrispASR, no --sherpa-segment-model needed.
    /// </summary>
    internal static string BuildArguments(
        string audioPath,
        string modelPath,
        string jsonBasePath,
        int numSpeakers,
        string? segmentModel,
        string method,
        string? embedder,
        string? defaultSegmentModel,
        string backend = "qwen3")
    {
        _ = segmentModel;
        _ = defaultSegmentModel;
        var args = $"--backend {backend} -m \"{modelPath}\" -f \"{audioPath}\" " +
                   $"--diarize-speakers --diarize-method {method} -ojf -of \"{jsonBasePath}\"";
        if (!string.IsNullOrEmpty(embedder))
            args += $" --diarize-embedder {embedder}";
        if (numSpeakers > 0)
            args += $" --diarize-max-speakers {numSpeakers}";
        return args;
    }

    /// <summary>
    /// Resolves the --diarize-embedder path for the foxnose method. CrispASR auto-downloads the
    /// WeSpeaker embedder from huggingface.co, which is unreachable from CN networks (WinHTTP/curl/wget
    /// all fail), so we pre-seed its cache (%USERPROFILE%\.cache\crispasr\wespeaker-resnet34-lm.gguf)
    /// from hf-mirror when missing. Returns null when not applicable or the download failed.
    /// </summary>
    private async Task<string?> EnsureEmbedderAsync(CancellationToken cancellationToken)
    {
        if (!string.Equals(DiarizeMethod, "foxnose", StringComparison.OrdinalIgnoreCase))
            return null;

        var cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "crispasr");
        var path = Path.Combine(cacheDir, "wespeaker-resnet34-lm.gguf");
        if (File.Exists(path) && new FileInfo(path).Length > 0)
            return path;

        Directory.CreateDirectory(cacheDir);
        _logger.LogInformation("WeSpeaker embedder missing; downloading from hf-mirror to {Path} ...", path);
        try
        {
            using var downloader = _serviceProvider.GetRequiredService<Centurion.Core.Operators.Download.Downloader>();
            await downloader.ProcessAsync(new OperatorsRequest<Centurion.Core.Operators.Download.Request.AriaDownloadRequest>
            {
                Payload = new Centurion.Core.Operators.Download.Request.AriaDownloadRequest
                {
                    Url = WespeakerEmbedderUrl,
                    FullSavePath = path,
                    MaxRetry = 3,
                    ProgressRefreshMs = 100
                }
            }, cancellationToken);
            return File.Exists(path) && new FileInfo(path).Length > 0 ? path : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("WeSpeaker embedder download failed: {Error}", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Builds CLI arguments using the current implementation's method and embedder (called by derived classes).
    /// </summary>
    /// <param name="audioPath">Path to the audio file to diarize.</param>
    /// <param name="modelPath">Path to the ASR model required by the CrispASR CLI.</param>
    /// <param name="jsonBasePath">Base path for the output JSON (without extension).</param>
    /// <param name="numSpeakers">Expected number of speakers; when greater than 0 it is used as the max-speaker limit.</param>
    /// <param name="segmentModel">Reserved segmentation model name (auto-managed by CrispASR; not part of the command line currently).</param>
    protected string BuildArguments(
        string audioPath, string modelPath, string jsonBasePath, int numSpeakers, string? segmentModel) =>
        BuildArguments(audioPath, modelPath, jsonBasePath, numSpeakers, segmentModel,
            DiarizeMethod, DiarizeEmbedder, DefaultSegmentModel);
}

