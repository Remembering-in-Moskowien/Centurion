using Centurion.Core.Workflow.Factories;
using Centurion.Abstractions;
using Centurion.Abstractions.Factories;
using Centurion.Abstractions.Pipeline;
using Centurion.Models.Workflow;
using Microsoft.Extensions.Logging;
using Centurion.Core.Capabilities.Managers.Runtime;
using Centurion.Core.Capabilities.Managers.Tools;
using Centurion.Core.Operators.Download;
using Centurion.Core.Operators.Download.Request;
namespace Centurion.Core.Workflow.Pipeline.Operators;

/// <summary>
/// Vocal separation operator (Demucs-rs, optional enhancement).
/// Separates the vocals track from the audio (--stems vocals) and writes it to State.VocalsPath,
/// for preferential consumption by transcription and speaker diarization. Runs only when WorkflowConfig.VocalSeparation is enabled;
/// a failure is non-fatal: it only logs a warning and falls back to the original audio.
/// <para>
/// Model acquisition: demucs-rs has hardcoded safetensors weight URLs and does not support a mirror environment variable,
/// so this operator pre-downloads the model into demucs-rs's cache directory before running demucs
/// (Windows: %LOCALAPPDATA%\demucs-rs\, Linux: ~/.cache/demucs-rs\, macOS: ~/Library/Caches/demucs-rs\);
/// if the official source download fails, it automatically falls back to the hf-mirror.com mirror so domestic networks can work.
/// The model base URL can be overridden via the modelBaseUrl of the demucsrs entry in metadata.json.
/// </para>
/// </summary>
public sealed class VocalSeparationOperator(
    IToolManagerFactory toolFactory,
    ProcessManager processManager,
    Centurion.Core.Operators.Download.Downloader downloader,
    ILogger<VocalSeparationOperator> logger) : PipelineOperatorBase<VocalSeparationOperator>(logger)
{
    /// <summary>The model download base address built into demucs-rs (corresponds to HF_BASE_URL).</summary>
    public const string DefaultModelBaseUrl = "https://huggingface.co/set-soft/audio_separation/resolve/main/Demucs/";

    private readonly IToolManagerFactory _toolFactory = toolFactory ?? throw new ArgumentNullException(nameof(toolFactory));
    private readonly ProcessManager _processManager = processManager ?? throw new ArgumentNullException(nameof(processManager));
    private readonly Centurion.Core.Operators.Download.Downloader _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));

    /// <summary>Display name of the operator in the pipeline.</summary>
    public override string Name => "Vocal Separation";

    /// <summary>
    /// Runs vocal separation: when the switch is on and no existing artifact is present, uses demucs-rs to separate the vocals track from the input audio,
    /// and writes the result into the <see cref="SubtitleWorkflowContext"/> state; a separation failure is non-fatal, only logging a warning and falling back to the original audio.
    /// </summary>
    /// <param name="context">Subtitle workflow context, providing configuration, state, and the input audio path.</param>
    /// <param name="cancellationToken">Token used to cancel the vocal separation process.</param>
    public override async Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken)
    {
        var config = context.Config;

        // 1. Switch (off by default: no value for pure speech material and time-consuming)
        if (!config.VocalSeparation)
        {
            LogInfo("Vocal separation disabled (VocalSeparation = false).");
            return;
        }

        // 2. Checkpoint: skip when already done and the artifact exists
        if (context.State.IsVocalsSeparated
            && !string.IsNullOrEmpty(context.State.VocalsPath)
            && File.Exists(context.State.VocalsPath))
        {
            LogInfo("Vocal separation already exists, skipping.");
            return;
        }

        // 3. Input audio (prefer the preprocessed audio, fall back to the converted audio)
        var inputPath = context.State.PreprocessedAudioPath
            ?? context.State.ConvertedAudioPath
            ?? config.InputFilePath;
        if (!File.Exists(inputPath))
        {
            LogWarning($"Audio file unavailable for vocal separation: {inputPath}");
            return;
        }

        var tempDir = context.State.PipelineTempDirectory;
        if (string.IsNullOrWhiteSpace(tempDir))
        {
            LogWarning("Pipeline temporary directory not set; skipping vocal separation.");
            return;
        }

        var tool = _toolFactory.Create("demucsrs", config.Device);
        try
        {
            // 4. Ensure the tool is ready (auto-download on first run)
            OnProgress(5, "Ensuring demucs-rs tool...");
            await tool.EnsureToolAsync(cancellationToken);

            // 5. Ensure the model is cached (auto-fallback to mirror when the official source fails; skip demucs on failure)
            OnProgress(10, $"Ensuring demucs model '{config.VocalSeparationModel}'...");
            if (!await EnsureDemucsModelAsync(tool, config.VocalSeparationModel, cancellationToken))
            {
                LogWarning("Demucs model unavailable; continuing with original audio.");
                return;
            }

            // 6. Run separation
            var outputDir = Path.Combine(tempDir, $"vocalsep_{Guid.NewGuid():N}");
            Directory.CreateDirectory(outputDir);
            var args = BuildArguments(config.VocalSeparationModel, inputPath, outputDir);

            LogInfo($"Running Demucs vocal separation (model {config.VocalSeparationModel})...");
            OnProgress(20, "Separating vocals (this may take a while)...");
            await _processManager.ExecuteAsync(tool.ExecutablePath, args, cancellationToken);

            // 7. Locate the vocals track (recursive search, tolerant of different output directory layouts)
            var vocalsFile = FindVocalsFile(outputDir);
            if (vocalsFile is null)
            {
                LogWarning("Demucs finished but no 'vocals' stem was found in output; continuing with original audio.");
                return;
            }

            var finalPath = Path.Combine(tempDir, $"vocals_{Guid.NewGuid():N}.wav");
            File.Move(vocalsFile, finalPath);

            context.State.VocalsPath = finalPath;
            context.State.IsVocalsSeparated = true;
            OnProgress(100, "Vocal separation completed.");
            LogInfo($"Vocals saved to: {finalPath}");

        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Non-fatal: a separation failure does not interrupt subtitle generation
            LogWarning($"Vocal separation failed; continuing with original audio. {ex.Message}");
        }
    }

    /// <summary>
    /// Ensures the demucs-rs model weights are cached in its expected cache directory.
    /// Returns directly when already cached; otherwise tries the official source and the hf-mirror mirror in turn.
    /// </summary>
    private async Task<bool> EnsureDemucsModelAsync(ToolManager tool, string model, CancellationToken cancellationToken)
    {
        var fileName = GetDemucsModelFileName(model);
        if (fileName is null)
        {
            LogWarning($"Unknown demucs model '{model}'; cannot pre-download its weights.");
            return false;
        }

        var targetPath = Path.Combine(GetDemucsCacheDir(), fileName);
        if (File.Exists(targetPath) && new FileInfo(targetPath).Length > 0)
        {
            LogInfo($"Demucs model already cached at {targetPath}");
            return true;
        }

        var baseUrl = string.IsNullOrWhiteSpace(tool.ModelBaseUrl) ? DefaultModelBaseUrl : tool.ModelBaseUrl!;
        var urls = new List<string> { baseUrl.TrimEnd('/') + "/" + fileName };
        var mirrorUrl = BuildMirrorUrl(urls[0]);
        if (!string.Equals(mirrorUrl, urls[0], StringComparison.OrdinalIgnoreCase))
            urls.Add(mirrorUrl);

        foreach (var url in urls)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                LogInfo($"Downloading Demucs model '{fileName}' from {url} ...");
                await _downloader.ProcessAsync(new OperatorsRequest<AriaDownloadRequest>
                {
                    Payload = new AriaDownloadRequest
                    {
                        Url = url,
                        FullSavePath = targetPath,
                        SplitThread = 8,
                        ServerConnection = 8,
                        MaxRetry = 3,
                        ProgressRefreshMs = 200
                    }
                }, cancellationToken);

                if (File.Exists(targetPath) && new FileInfo(targetPath).Length > 0)
                {
                    LogInfo($"Demucs model ready at {targetPath}");
                    return true;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogWarning($"Demucs model download failed ({url}): {ex.Message}");
                TryDeletePartialFile(targetPath);
            }
        }

        LogWarning($"Demucs model '{fileName}' could not be downloaded from any source. Check your network or proxy.");
        return false;
    }

    /// <summary>
    /// Builds Demucs-rs command-line arguments (internal for unit testing).
    /// Real CLI: demucs.exe -m &lt;model&gt; -s vocals -o &lt;outputDir&gt; &lt;input&gt;
    /// Output layout: &lt;outputDir&gt;/&lt;model&gt;/&lt;input-basename&gt;/vocals.wav (located recursively by FindVocalsFile).
    /// </summary>
    internal static IReadOnlyList<string> BuildArguments(string model, string inputPath, string outputDir) =>
        ["-m", model, "-s", "vocals", "-o", outputDir, inputPath];

    /// <summary>
    /// Recursively searches the Demucs output directory for the vocals track file (tolerant of version/layout differences).
    /// </summary>
    internal static string? FindVocalsFile(string outputDir)
    {
        if (!Directory.Exists(outputDir))
            return null;

        var candidates = Directory.GetFiles(outputDir, "vocals.wav", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(outputDir, "vocals.flac", SearchOption.AllDirectories))
            .ToList();
        return candidates.FirstOrDefault();
    }

    /// <summary>
    /// Model name to safetensors file name mapping (consistent with demucs-rs metadata.rs; unknown returns null).
    /// </summary>
    internal static string? GetDemucsModelFileName(string model) => model.Trim().ToLowerInvariant() switch
    {
        "htdemucs" => "htdemucs.safetensors",
        "htdemucs_6s" or "htdemucs-6s" => "htdemucs_6s.safetensors",
        "htdemucs_ft" or "htdemucs-ft" => "htdemucs_ft.safetensors",
        _ => null
    };

    /// <summary>
    /// The demucs-rs model cache directory (corresponds to its dirs::cache_dir().join("demucs-rs")).
    /// Windows: %LOCALAPPDATA%\demucs-rs; Linux: ~/.cache/demucs-rs; macOS: ~/Library/Caches/demucs-rs.
    /// </summary>
    internal static string GetDemucsCacheDir()
    {
        if (OperatingSystem.IsWindows())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "demucs-rs");

        if (OperatingSystem.IsMacOS())
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Caches", "demucs-rs");

        var xdg = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        var cacheBase = string.IsNullOrWhiteSpace(xdg)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache")
            : xdg;
        return Path.Combine(cacheBase, "demucs-rs");
    }

    /// <summary>
    /// Converts a HuggingFace official URL into an hf-mirror.com mirror URL; non-official URLs are returned unchanged.
    /// </summary>
    internal static string BuildMirrorUrl(string url) =>
        url.Replace("https://huggingface.co/", "https://hf-mirror.com/", StringComparison.OrdinalIgnoreCase);

    private static void TryDeletePartialFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Ignore cleanup failures; they do not affect the main flow
        }
    }
}
