using System.Globalization;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Centurion.Core.Utils.Infrastructure;
namespace Centurion.Core.Capabilities.Update;

/// <summary>
/// Self-update implementation based on the GitHub Releases API.
/// Flow: query the latest release -> semver comparison -> match asset by platform ->
/// download and extract into a staging directory -> generate the update script (which performs the replacement after the process exits).
/// </summary>
public sealed class GitHubUpdateService : IUpdateService
{
    private const string Repository = "Remembering-in-Moskowien/Centurion";

    private readonly HttpClient _http;
    private readonly ILogger<GitHubUpdateService> _logger;
    private readonly DateTimeOffset? _buildDate;

    /// <summary>
    /// Creates a service instance, initializes the GitHub HTTP client, and reads the local build date.
    /// </summary>
    /// <param name="logger">The logger used to record the update process.</param>
    public GitHubUpdateService(ILogger<GitHubUpdateService> logger)
    {
        _logger = logger;
        _http = new HttpClient { BaseAddress = new Uri("https://api.github.com"), Timeout = TimeSpan.FromMinutes(5) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Centurion/self-update");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

        _buildDate = ReadBuildDate();
    }

    /// <summary>Build date of the current local program (UTC); null when it cannot be read.</summary>
    public DateTimeOffset? BuildDate => _buildDate;

    /// <inheritdoc />
    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        var release = await GetLatestReleaseAsync(cancellationToken);
        if (release is null)
            return new UpdateCheckResult(false, null, "No GitHub releases found yet — nothing to update to. 📭");

        // Compare the build date against the release publication time; fall back to semver comparison when date info is missing.
        if (!IsNewer(BuildDate, release.PublishedAt, release.TagName))
            return new UpdateCheckResult(false, release, null);

        _logger.LogInformation(
            "Update available: build {Local} -> {Remote} (released {Published:yyyy-MM-dd})",
            BuildDate?.ToString("yyyy-MM-dd") ?? "unknown", release.TagName, release.PublishedAt);
        return new UpdateCheckResult(true, release, null);
    }

    /// <inheritdoc />
    public async Task<UpdateStageResult> StageAsync(UpdateCheckResult check, string? assetName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(check.Latest);

        var release = check.Latest;
        var rid = GetRuntimeIdentifier();
        var matched = MatchAsset(release.Assets, rid, assetName)
            ?? throw new InvalidOperationException(
                $"No matching release asset for '{rid}' ({(string.IsNullOrWhiteSpace(assetName) ? "auto" : $"preferred: {assetName}")}). " +
                "Available assets: " + string.Join(", ", release.Assets.Select(a => a.Name)));

        var asset = release.Assets.First(a => a.Name == matched);
        var tagDir = string.Concat(release.TagName.Where(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_'));
        if (tagDir.Length == 0) tagDir = "latest";

        // Update staging directory: placed under the program root in staging (retained across runs; not auto-cleaned before the user runs the apply script).
        var stagingDir = Path.Combine(AppContext.BaseDirectory, "staging", "CenturionUpdate", tagDir);
        if (Directory.Exists(stagingDir))
            Directory.Delete(stagingDir, recursive: true);
        Directory.CreateDirectory(stagingDir);

        var payloadDir = Path.Combine(stagingDir, "payload");
        Directory.CreateDirectory(payloadDir);

        var zipPath = Path.Combine(stagingDir, asset.Name);
        _logger.LogInformation("Downloading {Asset} ({Size} bytes)...", asset.Name, asset.SizeBytes);
        await DownloadAsync(asset.DownloadUrl, zipPath, cancellationToken);

        ExtractZipSafely(zipPath, payloadDir);

        var scriptPath = CreateApplyScript(stagingDir, payloadDir, tagDir);
        return new UpdateStageResult(stagingDir, payloadDir, scriptPath, asset);
    }

    // ------------------------------------------------------------------
    // GitHub API
    // ------------------------------------------------------------------

    private async Task<GitHubReleaseInfo?> GetLatestReleaseAsync(CancellationToken cancellationToken)
    {
        // When all repository releases are pre-releases, /releases/latest returns 404 (GitHub only serves stable releases at that endpoint);
        // fall back to listing recent releases and take the newest non-draft entry (including pre-releases), so the alpha/beta chain can also self-update.
        var latest = await TryGetReleaseAsync($"/repos/{Repository}/releases/latest", cancellationToken);
        if (latest is not null)
            return latest;

        using var listResponse = await _http.GetAsync($"/repos/{Repository}/releases?per_page=10", cancellationToken);
        listResponse.EnsureSuccessStatusCode();
        await using var listStream = await listResponse.Content.ReadAsStreamAsync(cancellationToken);
        var dtos = await JsonSerializer.DeserializeAsync<List<ReleaseDto>>(listStream, JsonOptions, cancellationToken);
        var newest = dtos?.FirstOrDefault(r => r.Draft != true);
        return newest is null ? null : ToReleaseInfo(newest);
    }

    private async Task<GitHubReleaseInfo?> TryGetReleaseAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(path, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var dto = await JsonSerializer.DeserializeAsync<ReleaseDto>(stream, JsonOptions, cancellationToken);
        return dto is null ? null : ToReleaseInfo(dto);
    }

    private static GitHubReleaseInfo ToReleaseInfo(ReleaseDto dto) => new(
        dto.TagName ?? dto.Name ?? "unknown",
        dto.Name ?? dto.TagName ?? "unknown",
        dto.PublishedAt,
        dto.Body,
        dto.Assets?.Select(a => new ReleaseAssetInfo(a.Name, a.BrowserDownloadUrl, a.Size)).ToList() ?? []);

    private async Task DownloadAsync(string url, string destinationPath, CancellationToken cancellationToken)
    {
        await GitHubDownloadProxy.DownloadWithFallbackAsync(
            url,
            candidate => DownloadCoreAsync(candidate, destinationPath, cancellationToken),
            IsNetworkFailure,
            reason => _logger.LogWarning("GitHub mirror download failed ({Reason}); trying next candidate...", reason),
            cancellationToken);
    }

    private async Task DownloadCoreAsync(string url, string destinationPath, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = File.Create(destinationPath);
        await source.CopyToAsync(target, cancellationToken);
    }

    private static bool IsNetworkFailure(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or OperationCanceledException
            or System.IO.IOException;


    // ------------------------------------------------------------------
    // Version comparison
    // ------------------------------------------------------------------

    /// <summary>
    /// Reads the local build date: prefers the <c>BuildDate</c> metadata embedded at compile time (UTC),
    /// falling back to the assembly file's last-write time (converted to UTC) when missing.
    /// </summary>
    private static DateTimeOffset? ReadBuildDate()
    {
        try
        {
            var assembly = Assembly.GetEntryAssembly();
            var metadata = assembly?.GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(attribute => attribute.Key == "BuildDate");
            if (metadata?.Value is { Length: > 0 } value
                && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            {
                return parsed;
            }
        }
        catch
        {
            // Ignore and try the file-timestamp fallback.
        }

        try
        {
            var location = Assembly.GetEntryAssembly()?.Location;
            if (!string.IsNullOrWhiteSpace(location) && File.Exists(location))
                return new DateTimeOffset(File.GetLastWriteTimeUtc(location));
        }
        catch
        {
            // Ignore.
        }

        return null;
    }

    /// <summary>
    /// Parses a semantic version tag (tolerates the v prefix, +build metadata, and -prerelease suffix).
    /// </summary>
    internal static bool TryParseVersion(string? raw, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var text = raw.Trim().TrimStart('v', 'V');
        var plus = text.IndexOf('+');
        if (plus >= 0) text = text[..plus];
        var dash = text.IndexOf('-');
        if (dash >= 0) text = text[..dash];
        if (!Version.TryParse(text, out var parsed))
            return false;
        version = parsed;
        return true;
    }

    /// <summary>
    /// Determines whether the remote is newer than local: compares by date when both the local build date and remote publication time are known;
    /// falls back to semver comparison (<see cref="IsNewer(string, string)"/>) when either is missing.
    /// </summary>
    internal static bool IsNewer(DateTimeOffset? localBuild, DateTimeOffset? remotePublished, string remoteTag)
    {
        if (localBuild is { } local && remotePublished is { } remote)
            return remote > local;

        var localRaw = localBuild?.ToString("yyyy-MM-dd") ?? "0.0.0";
        return IsNewer(localRaw, remoteTag);
    }

    /// <summary>
    /// Determines whether the remote version is newer than the local version (semver first, falling back to string comparison).
    /// </summary>
    internal static bool IsNewer(string localRaw, string remoteRaw)
    {
        if (TryParseVersion(localRaw, out var local) && TryParseVersion(remoteRaw, out var remote))
            return remote > local;

        return string.Compare(
            remoteRaw.TrimStart('v', 'V'),
            localRaw.TrimStart('v', 'V'),
            StringComparison.OrdinalIgnoreCase) > 0;
    }

    // ------------------------------------------------------------------
    // Platform and asset matching
    // ------------------------------------------------------------------

    /// <summary>Current runtime identifier (win-x64 / linux-x64 / osx-arm64, etc.).</summary>
    public static string GetRuntimeIdentifier()
    {
        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            _ => "x64"
        };

        if (OperatingSystem.IsWindows()) return $"win-{arch}";
        if (OperatingSystem.IsLinux()) return $"linux-{arch}";
        if (OperatingSystem.IsMacOS()) return $"osx-{arch}";
        return $"win-{arch}";
    }

    /// <summary>
    /// Matches the release package for the current platform in the asset list.
    /// Match priority: manually specified name -> exact <c>Centurion-{rid}.zip</c> ->
    /// the largest file whose name contains the rid and ends with .zip.
    /// </summary>
    internal static string? MatchAsset(IReadOnlyList<ReleaseAssetInfo> assets, string rid, string? preferredName)
    {
        if (assets.Count == 0)
            return null;

        if (!string.IsNullOrWhiteSpace(preferredName))
            return assets.FirstOrDefault(a => string.Equals(a.Name, preferredName, StringComparison.OrdinalIgnoreCase))?.Name;

        var exact = assets.FirstOrDefault(a =>
            string.Equals(a.Name, $"Centurion-{rid}.zip", StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
            return exact.Name;

        // Support compact variants (win-x64 -> win64), covering names like centurion-win64.zip.
        var compactRid = rid.Replace("-x64", "64", StringComparison.Ordinal)
            .Replace("-arm64", "arm64", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal);
        return assets
            .Where(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                        && (a.Name.Contains(rid, StringComparison.OrdinalIgnoreCase)
                            || (compactRid.Length > 0
                                && a.Name.Contains(compactRid, StringComparison.OrdinalIgnoreCase))))
            .OrderByDescending(a => a.SizeBytes)
            .Select(a => a.Name)
            .FirstOrDefault();
    }

    // ------------------------------------------------------------------
    // Extraction and update script
    // ------------------------------------------------------------------

    /// <summary>
    /// Safely extracts a zip to the target directory, preventing zip-slip (entry path escape).
    /// </summary>
    internal static void ExtractZipSafely(string zipPath, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        var root = Path.GetFullPath(destinationDirectory);

        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            var fullPath = Path.GetFullPath(Path.Combine(root, entry.FullName));
            if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new InvalidDataException($"Unsafe zip entry path rejected: {entry.FullName}");

            var isDirectory = entry.FullName.EndsWith("/", StringComparison.Ordinal) || entry.FullName.EndsWith("\\", StringComparison.Ordinal);
            var target = fullPath;

            if (isDirectory)
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    /// <summary>
    /// Generates a deferred-apply update script: wait for the old process to exit -> copy new files -> clean up -> restart.
    /// Windows generates .cmd; other platforms generate .sh.
    /// Accepts only a sanitized version identifier (letters/digits/.-_), preventing remote tags from injecting script commands.
    /// </summary>
    private string CreateApplyScript(string stagingDir, string payloadDir, string tagDir)
    {
        var appDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var exeBase = Path.GetFileNameWithoutExtension(Environment.ProcessPath) ?? "Centurion.Cli";

        if (OperatingSystem.IsWindows())
        {
            var script = Path.Combine(stagingDir, "apply-update.cmd");
            File.WriteAllText(script, string.Join("\r\n",
                "@echo off",
                "chcp 65001 >nul",
                "setlocal",
                "echo.",
                $"echo 🔄 Centurion update: applying {tagDir} ...",
                "timeout /t 3 /nobreak >nul",
                $"taskkill /f /im \"{exeBase}.exe\" >nul 2>&1",
                $"xcopy /y /e /q \"{payloadDir}\\*\" \"{appDir}\\\" >nul",
                "if errorlevel 1 goto fail",
                "echo ✅ Update applied. Restarting Centurion ...",
                $"start \"\" \"{appDir}\\{exeBase}.exe\"",
                $"rmdir /s /q \"{stagingDir}\"",
                "del \"%~f0\"",
                "exit /b 0",
                ":fail",
                $"echo ❌ Update failed. Files kept at {stagingDir}",
                "pause",
                "exit /b 1"));
            return script;
        }

        var sh = Path.Combine(stagingDir, "apply-update.sh");
        File.WriteAllText(sh, string.Join("\n",
            "#!/usr/bin/env bash",
            "set -u",
            $"echo \"🔄 Centurion update: applying {tagDir} ...\"",
            "sleep 3",
            $"pkill -f '{exeBase}' >/dev/null 2>&1 || true",
            $"cp -R \"{payloadDir}/.\" \"{appDir}/\"",
            "echo \"✅ Update applied. Restarting Centurion ...\"",
            $"nohup \"{appDir}/{exeBase}\" >/dev/null 2>&1 &",
            $"rm -rf \"{stagingDir}\"",
            "exit 0"));
        return sh;
    }

    // ------------------------------------------------------------------
    // JSON DTO
    // ------------------------------------------------------------------

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed class ReleaseDto
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("published_at")] public DateTimeOffset? PublishedAt { get; set; }
        [JsonPropertyName("body")] public string? Body { get; set; }
        [JsonPropertyName("draft")] public bool? Draft { get; set; }
        [JsonPropertyName("assets")] public List<AssetDto>? Assets { get; set; }
    }

    private sealed class AssetDto
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("browser_download_url")] public string BrowserDownloadUrl { get; set; } = "";
        [JsonPropertyName("size")] public long Size { get; set; }
    }
}
