namespace Centurion.Core.Utils.Infrastructure;

/// <summary>
/// GitHub download acceleration proxy: rewrites github.com / raw.githubusercontent.com download URLs
/// to try multiple 520-class acceleration mirrors in turn, falling back to a direct connection only after all fail.
/// Injected by the CLI entry point via --github-proxy / --no-github-proxy; the built-in mirror chain is enabled by default.
/// </summary>
public static class GitHubDownloadProxy
{
    /// <summary>Default acceleration mirror chain (tried in order; earlier entries take priority).</summary>
    public static readonly string[] DefaultMirrors =
    [
        "https://gh-proxy.com/",
        "https://ghproxy.net/",
        "https://ghfast.top/",
        "https://gh.llkk.cc/"
    ];

    /// <summary>Mirror prefix specified by the user via --github-proxy; the default mirror chain is used when null.</summary>
    public static string? ProxyPrefix { get; set; }

    /// <summary>Whether acceleration is disabled (--no-github-proxy); when disabled, only the direct connection is used.</summary>
    public static bool Disabled { get; set; }

    /// <summary>Whether the URL is an acceleration-eligible GitHub download URL.</summary>
    /// <param name="url">The original URL.</param>
    public static bool IsGithubDownloadUrl(string? url) =>
        !string.IsNullOrWhiteSpace(url)
        && (url!.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("https://raw.githubusercontent.com/", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Generates candidate download URLs in priority order: user mirror → default mirror chain → original URL (direct).
    /// Non-GitHub URLs return the original URL only.
    /// </summary>
    /// <param name="url">The original download URL.</param>
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
    /// Download with automatic fallback: tries each candidate in turn per <see cref="CandidateUrls"/>,
    /// automatically switching to the next candidate on network-class failures; throws when the last candidate (direct) fails.
    /// </summary>
    /// <param name="url">The original download URL.</param>
    /// <param name="download">Callback that performs the download for a given URL.</param>
    /// <param name="isRetryable">Determines whether an exception is a network-class failure eligible for fallback.</param>
    /// <param name="logFallback">Logs when switching candidates (argument is the failure reason).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
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
