using Centurion.Core.Managers;
using Centurion.Core.Operators;
using Centurion.Core.Operators.Request;
using Centurion.Abstractions;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Utils;

/// <summary>
/// CrispASR 说话人分割模型缓存管理器。
/// CrispASR 的 --diarize 会按方法自动从 HuggingFace 下载嵌入/分割模型到
/// 用户缓存目录（~/.cache/crispasr/），官方源在国内网络常不可达；
/// 本类在运行 diarize 前把所需模型预下载到位（官方源失败自动回退 hf-mirror.com 镜像），
/// 使 CrispASR 直接命中缓存（"using cached"），保证分割可用。
/// </summary>
public sealed class CrispAsrModelCache(
    Operators.Downloader downloader,
    ILogger<CrispAsrModelCache> logger)
{
    /// <summary>官方 HuggingFace 模型基础地址。</summary>
    public const string DefaultModelBaseUrl = "https://huggingface.co/";

    private readonly Operators.Downloader _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
    private readonly ILogger<CrispAsrModelCache> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>模型文件名 → 官方下载地址（resolve/main 直链）映射。</summary>
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
    /// 确保给定 diarize 方法所需的模型已缓存。
    /// </summary>
    /// <param name="method">diarize 方法名（foxnose / pyannote 等）。</param>
    /// <param name="cancellationToken">用于取消下载的取消标记。</param>
    /// <returns>所需模型全部就绪返回 true；任一模型无法获取返回 false。</returns>
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
    /// 确保单个模型已缓存：命中缓存直接返回；否则依次尝试官方源与 hf-mirror 镜像。
    /// </summary>
    /// <param name="fileName">模型文件名（须在 <see cref="ModelUrls"/> 中登记）。</param>
    /// <param name="cancellationToken">用于取消下载的取消标记。</param>
    /// <returns>模型就绪返回 true，否则 false。</returns>
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
    /// 计算给定 diarize 方法所需预下载的模型文件名列表（internal，便于单元测试）。
    /// </summary>
    /// <param name="method">diarize 方法名。</param>
    /// <returns>模型文件名列表；方法无需预下载时为空。</returns>
    internal static IReadOnlyList<string> GetRequiredModels(string method) => method.Trim().ToLowerInvariant() switch
    {
        "foxnose" => ["wespeaker-resnet34-lm.gguf"],
        "pyannote" => ["pyannote-seg-3.0.gguf", "titanet-large.gguf"],
        _ => []
    };

    /// <summary>
    /// CrispASR 的模型缓存目录（对应其 cache dir 实现）。
    /// Windows: %USERPROFILE%\.cache\crispasr；Linux: ~/.cache/crispasr；macOS: ~/.cache/crispasr。
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
    /// 将 HuggingFace 官方地址转换为 hf-mirror.com 镜像地址；非官方地址原样返回。
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
            // 忽略清理失败，不影响主流程
        }
    }
}
