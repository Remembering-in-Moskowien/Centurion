namespace Centurion.Core.Capabilities.Update;

/// <summary>
/// Describes a downloadable asset in a GitHub Release (a zip package, etc.).
/// </summary>
/// <param name="Name">Asset file name, e.g. Centurion-win-x64.zip.</param>
/// <param name="DownloadUrl">Direct download link (browser_download_url).</param>
/// <param name="SizeBytes">Asset size in bytes.</param>
public sealed record ReleaseAssetInfo(string Name, string DownloadUrl, long SizeBytes);

/// <summary>
/// Summary information of a GitHub Release (taken from the releases/latest endpoint).
/// </summary>
/// <param name="TagName">Version tag, e.g. v0.2.0.</param>
/// <param name="Name">Release title.</param>
/// <param name="PublishedAt">Publication time (UTC).</param>
/// <param name="Body">Release notes (Markdown).</param>
/// <param name="Assets">List of assets.</param>
public sealed record GitHubReleaseInfo(
    string TagName,
    string Name,
    DateTimeOffset? PublishedAt,
    string? Body,
    IReadOnlyList<ReleaseAssetInfo> Assets);

/// <summary>
/// Result of an update check.
/// </summary>
/// <param name="HasUpdate">Whether a newer version is available to update to.</param>
/// <param name="Latest">Information of the latest Release (non-null when an update exists).</param>
/// <param name="Reason">Human-readable reason when there is no update (e.g. "the repository has no releases yet").</param>
public sealed record UpdateCheckResult(bool HasUpdate, GitHubReleaseInfo? Latest, string? Reason);

/// <summary>
/// Update staging result: the new version has been downloaded and extracted, and the update script has been generated.
/// </summary>
/// <param name="StagingDirectory">Staging root directory.</param>
/// <param name="PayloadDirectory">Directory of the extracted new-version files.</param>
/// <param name="ScriptPath">Absolute path of the update script (run it to complete the replacement).</param>
/// <param name="Asset">The asset that was downloaded.</param>
public sealed record UpdateStageResult(
    string StagingDirectory,
    string PayloadDirectory,
    string ScriptPath,
    ReleaseAssetInfo Asset);

/// <summary>
/// Self-update service: check GitHub Releases, compare against the local version, and download and stage the new version.
/// </summary>
public interface IUpdateService
{
    /// <summary>Build date of the local program (UTC); null when it cannot be read.</summary>
    DateTimeOffset? BuildDate { get; }

    /// <summary>
    /// Check whether a newer version than the local one exists on GitHub.
    /// </summary>
    /// <exception cref="HttpRequestException">Thrown when the GitHub API cannot be reached.</exception>
    Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Download the Release asset matching the current platform, extract it to a temporary staging directory, and generate the update script.
    /// </summary>
    /// <param name="check">The update-confirmed result returned by <see cref="CheckAsync"/>.</param>
    /// <param name="assetName">Manually specified asset file name; when empty, matched automatically by platform.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    Task<UpdateStageResult> StageAsync(UpdateCheckResult check, string? assetName, CancellationToken cancellationToken);
}
