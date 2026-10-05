using Centurion.Abstractions;
using Microsoft.Extensions.Logging;
using Centurion.Core.Operators.Download;
using Centurion.Core.Operators.Download.Request;
namespace Centurion.Core.Utils.Infrastructure;

/// <summary>
/// Cache manager for CrispASR speaker diarization models.
/// CrispASR's --diarize automatically downloads the embedding/segmentation models from HuggingFace
/// into the user cache directory (~/.cache/crispasr/), which is often unreachable from networks in China;
/// this class pre-downloads the required models before running diarize (auto-falling back to the
/// hf-mirror.com mirror when the official source fails), so CrispASR hits the cache directly
/// ("using cached") and diarization stays usable.
/// </summary>
public sealed class CrispAsrModelCache(
    Centurion.Core.Operators.Download.Downloader downloader,
    ILogger<CrispAsrModelCache> logger)
{
    /// <summary>Official HuggingFace model base URL.</summary>
    public const string DefaultModelBaseUrl = "https://huggingface.co/";

    private readonly Centurion.Core.Operators.Download.Downloader _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
    private readonly ILogger<CrispAsrModelCache> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>Mapping from model file name → official download URL (resolve/main direct link).</summary>
    private static readonly IReadOnlyDictionary<string, string> ModelUrls = new Dictionary<string, string>
    {
        ["wespeaker-resnet34-lm.gguf"] =
            "https://huggingface.co/cstr/wespeaker-resnet34-lm-GGUF/resolve/main/wespeaker-resnet34-lm.gguf",
        ["pyannote-seg-3.0.gguf"] =
            "https://huggingface.co/cstr/pyannote-v3-segmentation-GGUF/resolve/main/pyannote-seg-3.0.gguf",
        ["titanet-large.gguf"] =
            "https://huggingface.co/cstr/titanet-large-GGUF/resolve/main/titanet-large.gguf"
    };

    /// <summary>
    /// Ensures the models required by the given diarize method are cached.
    /// </summary>
    /// <param name="method">Diarize method name (foxnose / pyannote, etc.).</param>
    /// <param name="cancellationToken">Cancellation token used to cancel downloads.</param>
    /// <returns>true when all required models are ready; false if any model cannot be obtained.</returns>
    public async Task<bool> EnsureModelsAsync(string method, CancellationToken cancellationToken = default)
    {
        var required = GetRequiredModels(method);
        if (required.Count == 0)
            return true;

        var ok = true;
        foreach (var fileName in required)
        {
            if (!await EnsureModelAsync(fileName, cancellationToken))
                ok = false;
        }

        return ok;
    }

    /// <summary>
    /// Ensures a single model is cached: returns immediately on a cache hit; otherwise tries the official source and the hf-mirror mirror in turn.
    /// </summary>
    /// <param name="fileName">Model file name (must be registered in <see cref="ModelUrls"/>).</param>
    /// <param name="cancellationToken">Cancellation token used to cancel downloads.</param>
    /// <returns>true when the model is ready; otherwise false.</returns>
    public async Task<bool> EnsureModelAsync(string fileName, CancellationToken cancellationToken = default)
    {
        if (!ModelUrls.TryGetValue(fileName, out var officialUrl))
        {
            _logger.LogWarning("Unknown CrispASR model '{FileName}'; cannot pre-download it.", fileName);
            return false;
        }

        var targetPath = Path.Combine(GetCacheDir(), fileName);
        if (File.Exists(targetPath) && new FileInfo(targetPath).Length > 0)
        {
            _logger.LogDebug("CrispASR model already cached at {Path}", targetPath);
            return true;
        }

        var urls = new List<string> { officialUrl };
        var mirrorUrl = BuildMirrorUrl(officialUrl);
        if (!string.Equals(mirrorUrl, officialUrl, StringComparison.OrdinalIgnoreCase))
            urls.Add(mirrorUrl);

        foreach (var url in urls)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                _logger.LogInformation("Downloading CrispASR model '{FileName}' from {Url} ...", fileName, url);
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
                    _logger.LogInformation("CrispASR model ready at {Path}", targetPath);
                    return true;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("CrispASR model download failed ({Url}): {Message}", url, ex.Message);
                TryDeletePartialFile(targetPath);
            }
        }

        _logger.LogWarning("CrispASR model '{FileName}' could not be downloaded from any source. Check your network or proxy.", fileName);
        return false;
    }

    /// <summary>
    /// Computes the list of model file names to pre-download for the given diarize method (internal, for unit testing).
    /// </summary>
    /// <param name="method">Diarize method name.</param>
    /// <returns>The list of model file names; empty when the method needs no pre-download.</returns>
    internal static IReadOnlyList<string> GetRequiredModels(string method) => method.Trim().ToLowerInvariant() switch
    {
        "foxnose" => ["wespeaker-resnet34-lm.gguf"],
        "pyannote" => ["pyannote-seg-3.0.gguf", "titanet-large.gguf"],
        _ => []
    };

    /// <summary>
    /// CrispASR's model cache directory (corresponding to its cache dir implementation).
    /// Windows: %USERPROFILE%\.cache\crispasr; Linux: ~/.cache/crispasr; macOS: ~/.cache/crispasr.
    /// </summary>
    internal static string GetCacheDir()
    {
        var xdg = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        var cacheBase = string.IsNullOrWhiteSpace(xdg)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache")
            : xdg;
        return Path.Combine(cacheBase, "crispasr");
    }

    /// <summary>
    /// Converts an official HuggingFace URL to an hf-mirror.com mirror URL; non-official URLs are returned unchanged.
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
