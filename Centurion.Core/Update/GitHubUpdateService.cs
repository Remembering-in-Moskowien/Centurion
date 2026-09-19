using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Update;

/// <summary>
/// 基于 GitHub Releases API 的自更新实现。
/// 流程：查询 latest release → 语义化版本对比 → 按平台匹配资产 →
/// 下载解压到暂存目录 → 生成更新脚本（进程退出后完成替换）。
/// </summary>
public sealed class GitHubUpdateService : IUpdateService
{
    private const string Repository = "Remembering-in-Moskowien/Centurion";

    private readonly HttpClient _http;
    private readonly ILogger<GitHubUpdateService> _logger;
    private readonly string? _localVersion;

    /// <summary>
    /// 创建服务实例，初始化 GitHub HTTP 客户端并读取本地版本号。
    /// </summary>
    /// <param name="logger">用于记录更新过程的日志器。</param>
    public GitHubUpdateService(ILogger<GitHubUpdateService> logger)
    {
        _logger = logger;
        _http = new HttpClient { BaseAddress = new Uri("https://api.github.com"), Timeout = TimeSpan.FromMinutes(5) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Centurion/self-update");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

        _localVersion = ReadLocalVersion();
    }

    /// <summary>当前本地应用版本号；无法读取时回退为 "0.0.0"。</summary>
    public string LocalVersion => _localVersion ?? "0.0.0";

    /// <inheritdoc />
    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        var release = await GetLatestReleaseAsync(cancellationToken);
        if (release is null)
            return new UpdateCheckResult(false, null, "No GitHub releases found yet — nothing to update to. 📭");

        if (!IsNewer(LocalVersion, release.TagName))
            return new UpdateCheckResult(false, release, null);

        _logger.LogInformation("Update available: {Local} -> {Remote}", LocalVersion, release.TagName);
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

        var stagingDir = Path.Combine(Path.GetTempPath(), "CenturionUpdate", tagDir);
        if (Directory.Exists(stagingDir))
            Directory.Delete(stagingDir, recursive: true);
        Directory.CreateDirectory(stagingDir);

        var payloadDir = Path.Combine(stagingDir, "payload");
        Directory.CreateDirectory(payloadDir);

        var zipPath = Path.Combine(stagingDir, asset.Name);
        _logger.LogInformation("Downloading {Asset} ({Size} bytes)...", asset.Name, asset.SizeBytes);
        await DownloadAsync(asset.DownloadUrl, zipPath, cancellationToken);

        ExtractZipSafely(zipPath, payloadDir);

        var scriptPath = CreateApplyScript(stagingDir, payloadDir, release.TagName);
        return new UpdateStageResult(stagingDir, payloadDir, scriptPath, asset);
    }

    // ------------------------------------------------------------------
    // GitHub API
    // ------------------------------------------------------------------

    private async Task<GitHubReleaseInfo?> GetLatestReleaseAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync($"/repos/{Repository}/releases/latest", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var dto = await JsonSerializer.DeserializeAsync<ReleaseDto>(stream, JsonOptions, cancellationToken);
        if (dto is null)
            return null;

        return new GitHubReleaseInfo(
            dto.TagName ?? dto.Name ?? "unknown",
            dto.Name ?? dto.TagName ?? "unknown",
            dto.PublishedAt,
            dto.Body,
            dto.Assets?.Select(a => new ReleaseAssetInfo(a.Name, a.BrowserDownloadUrl, a.Size)).ToList() ?? []);
    }

    private async Task DownloadAsync(string url, string destinationPath, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = File.Create(destinationPath);
        await source.CopyToAsync(target, cancellationToken);
    }

    // ------------------------------------------------------------------
    // 版本比较
    // ------------------------------------------------------------------

    /// <summary>
    /// 从程序集版本信息读取本地版本（<c>&lt;Version&gt;</c> 配置）。
    /// </summary>
    private static string? ReadLocalVersion()
    {
        try
        {
            var assembly = Assembly.GetEntryAssembly();
            var info = assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(info))
            {
                var plus = info.IndexOf('+');
                return plus >= 0 ? info[..plus] : info;
            }
            return assembly?.GetName().Version?.ToString(3);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 解析语义化版本标签（容忍 v 前缀、+build 元数据、-prerelease 后缀）。
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
    /// 判断远端版本是否比本地版本新（语义化版本优先，回退字符串比较）。
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
    // 平台与资产匹配
    // ------------------------------------------------------------------

    /// <summary>当前运行时平台标识（win-x64 / linux-x64 / osx-arm64 等）。</summary>
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
    /// 在资产列表中匹配当前平台的发布包。
    /// 匹配优先级：手动指定名 → 精确 <c>Centurion-{rid}.zip</c> →
    /// 名称包含 rid 且以 .zip 结尾的最大文件。
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

        return assets
            .Where(a => a.Name.Contains(rid, StringComparison.OrdinalIgnoreCase)
                        && a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(a => a.SizeBytes)
            .Select(a => a.Name)
            .FirstOrDefault();
    }

    // ------------------------------------------------------------------
    // 解压与更新脚本
    // ------------------------------------------------------------------

    /// <summary>
    /// 安全解压 zip 到目标目录，防止 zip-slip（entry 路径逃逸）。
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
    /// 生成延迟应用的更新脚本：等待旧进程退出 → 拷贝新文件 → 清理 → 重启。
    /// Windows 生成 .cmd，其他平台生成 .sh。
    /// </summary>
    private string CreateApplyScript(string stagingDir, string payloadDir, string tagName)
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
                $"echo 🔄 Centurion update: applying {tagName} ...",
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
            $"echo \"🔄 Centurion update: applying {tagName} ...\"",
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
        [JsonPropertyName("assets")] public List<AssetDto>? Assets { get; set; }
    }

    private sealed class AssetDto
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("browser_download_url")] public string BrowserDownloadUrl { get; set; } = "";
        [JsonPropertyName("size")] public long Size { get; set; }
    }
}
