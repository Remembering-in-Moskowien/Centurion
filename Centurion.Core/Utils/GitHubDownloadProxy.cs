namespace Centurion.Core.Utils;

/// <summary>
/// GitHub 下载加速代理：将 github.com / raw.githubusercontent.com 的下载 URL
/// 依次尝试多个 520 类加速镜像，全部失败后再回退直连。
/// 由 CLI 入口按 --github-proxy / --no-github-proxy 注入；默认启用内置镜像链。
/// </summary>
public static class GitHubDownloadProxy
{
    /// <summary>默认加速镜像链（按顺序尝试，越靠前越优先）。</summary>
    public static readonly string[] DefaultMirrors =
    [
        "https://ghfast.top/",
        "https://gh-proxy.com/",
        "https://gh.llkk.cc/",
        "https://github.moeyy.xyz/"
    ];

    /// <summary>用户通过 --github-proxy 指定的镜像前缀；为 null 时使用默认镜像链。</summary>
    public static string? ProxyPrefix { get; set; }

    /// <summary>是否禁用加速（--no-github-proxy）；禁用后仅直连。</summary>
    public static bool Disabled { get; set; }

    /// <summary>URL 是否为可加速的 GitHub 下载地址。</summary>
    /// <param name="url">原始 URL。</param>
    public static bool IsGithubDownloadUrl(string? url) =>
        !string.IsNullOrWhiteSpace(url)
        && (url!.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("https://raw.githubusercontent.com/", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 生成按优先顺序排列的候选下载 URL：用户镜像 → 默认镜像链 → 原 URL（直连）。
    /// 非 GitHub URL 仅返回原 URL。
    /// </summary>
    /// <param name="url">原始下载 URL。</param>
    public static IEnumerable<string> CandidateUrls(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !IsGithubDownloadUrl(url))
        {
            if (url is not null)
                yield return url;
            yield break;
        }

        if (!Disabled)
        {
            if (!string.IsNullOrWhiteSpace(ProxyPrefix))
            {
                yield return ProxyPrefix + url;
            }
            else
            {
                foreach (var mirror in DefaultMirrors)
                    yield return mirror + url;
            }
        }

        yield return url;
    }

    /// <summary>
    /// 带自动回退的下载：按 <see cref="CandidateUrls"/> 依次尝试每个候选，
    /// 网络类失败自动切下一个候选，最后一个候选（直连）失败时抛出。
    /// </summary>
    /// <param name="url">原始下载 URL。</param>
    /// <param name="download">对给定 URL 执行下载的回调。</param>
    /// <param name="isRetryable">判断异常是否属于可回退的网络类失败。</param>
    /// <param name="logFallback">切换候选时记录日志（参数为失败原因）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public static async Task DownloadWithFallbackAsync(
        string url,
        Func<string, Task> download,
        Func<Exception, bool> isRetryable,
        Action<string> logFallback,
        CancellationToken cancellationToken)
    {
        var candidates = CandidateUrls(url).ToList();
        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            try
            {
                await download(candidate);
                return;
            }
            catch (Exception ex) when (isRetryable(ex) && i < candidates.Count - 1)
            {
                logFallback(ex.Message);
            }
        }
    }
}
