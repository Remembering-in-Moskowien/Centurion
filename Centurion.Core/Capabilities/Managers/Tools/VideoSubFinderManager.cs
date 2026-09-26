using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Centurion.Abstractions;
using Microsoft.Extensions.Logging;
using SharpCompress.Archives;
using Centurion.Core.Utils.Infrastructure;
namespace Centurion.Core.Capabilities.Managers.Tools;

/// <summary>
/// 下载并缓存 VideoSubFinder CLI，供 OCR 字幕帧检测使用。
/// 下载源（Windows x64，按序尝试，逐源 元数据→下载→SHA-256→解压→安装，失败自动切下一个）：
///   1. GitHub lionc2240/autovsf 镜像的官方 VideoSubFinder_6.10_x64.zip（含 SHA-256 digest）；
///   2. SourceForge 镜像直链（twds.dl，用户指定；同一官方包，SHA-256 已知）；
///   3. SourceForge best_release.json 动态解析（无 SHA-256，部分网络不可达）。
///   官方包内含 VideoSubFinderWXW.exe（支持无头命令行模式）。
/// 安装失败（下载不可达 / 包内无可用可执行文件）返回 null，OCR 主链路降级为 FFmpeg 抽帧。
/// </summary>
public sealed class VideoSubFinderManager(
    ITempDirectoryManager tempManager,
    ILogger<VideoSubFinderManager> logger)
{
    /// <summary>Windows 官方包的 GitHub 镜像（SourceForge 直连在部分网络不可达，故优先走 GitHub）。</summary>
    private const string WindowsGitHubReleaseApi =
        "https://api.github.com/repos/lionc2240/autovsf/releases/tags/VideoSubFinder_6.10_x64";

    private const string LinuxMacGitHubReleaseApi =
        "https://api.github.com/repos/eritpchy/videosubfinder-cli/releases/latest";

    private const string SourceForgeReleaseApi =
        "https://sourceforge.net/projects/videosubfinder/best_release.json";

    /// <summary>SourceForge 官方 Windows 包镜像直链（用户指定；与 GitHub autovsf 同一官方包）。</summary>
    private const string WindowsSourceForgeDirectZip =
        "https://twds.dl.sourceforge.net/project/videosubfinder/VideoSubFinder_6.10_x64.zip";

    /// <summary>官方 VideoSubFinder_6.10_x64.zip 的已知 SHA-256（与 autovsf 资产一致）。</summary>
    private const string WindowsSourceForgeZipSha256 =
        "3c0cc03793ec9753a6a4ee8a91c1d226c20b80aab901718f7c97d4fcb3580c0e";

    private static readonly HttpClient HttpClient = CreateHttpClient();
    private static readonly SemaphoreSlim InstallGate = new(1, 1);
    private static readonly string ToolsRoot = Path.Combine(AppContext.BaseDirectory, "tools", "videosubfinder");

    /// <summary>确保 CLI 已安装并返回路径；平台不受支持或安装失败时返回 null。</summary>
    public async Task<string?> EnsureInstalledAsync(CancellationToken cancellationToken)
    {
        var installed = FindInstalledExecutable();
        if (installed is not null)
            return installed;

        await InstallGate.WaitAsync(cancellationToken);
        try
        {
            installed = FindInstalledExecutable();
            if (installed is not null)
                return installed;

            if (!TryGetAssetNames(out var assetName, out var executableNames))
            {
                logger.LogInformation("VideoSubFinder auto-download is not available for this OS or architecture.");
                return null;
            }

            try
            {
                return await TryInstallFromSourcesAsync(assetName, executableNames, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "VideoSubFinder auto-download failed; OCR will use FFmpeg frame extraction.");
                return null;
            }
        }
        finally
        {
            InstallGate.Release();
        }
    }

    private string? FindInstalledExecutable()
    {
        if (!Directory.Exists(ToolsRoot))
            return null;

        foreach (var executableName in GetExecutableNames())
        {
            var match = Directory.EnumerateFiles(ToolsRoot, executableName, SearchOption.AllDirectories)
                .FirstOrDefault();
            if (match is not null)
                return match;
        }

        return null;
    }

    private async Task<string?> DownloadAndInstallAsync(
        ReleaseArchive release,
        string assetName,
        IReadOnlyList<string> executableNames,
        CancellationToken cancellationToken)
    {
        await using var tempDir = await tempManager.CreateTempDirectoryAsync("videosubfinder_");
        var archivePath = Path.Combine(tempDir.Path, assetName);
        var extractPath = Path.Combine(tempDir.Path, "extract");
        Directory.CreateDirectory(extractPath);

        logger.LogInformation("Downloading VideoSubFinder CLI from {Url}.", release.Url);
        await DownloadAsync(release.Url, archivePath, cancellationToken);
        if (!string.IsNullOrWhiteSpace(release.Sha256))
            await VerifySha256Async(archivePath, release.Sha256, cancellationToken);

        ExtractArchive(archivePath, extractPath);
        var executable = executableNames
            .SelectMany(name => Directory.EnumerateFiles(extractPath, name, SearchOption.AllDirectories))
            .FirstOrDefault();
        if (executable is null)
        {
            logger.LogWarning(
                "Downloaded VideoSubFinder archive '{Asset}' did not contain any usable executable ({Names}).",
                assetName, string.Join(", ", executableNames));
            Directory.CreateDirectory(ToolsRoot);
            await File.WriteAllTextAsync(RejectedReleasePath, GetReleaseFingerprint(release), cancellationToken);
            return null;
        }

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(executable,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(ToolsRoot)!);
        var installPath = Path.Combine(ToolsRoot, Path.GetFileNameWithoutExtension(assetName));
        if (Directory.Exists(installPath))
            installPath += "_" + Guid.NewGuid().ToString("N");

        var relativeExecutable = Path.GetRelativePath(extractPath, executable);
        Directory.Move(extractPath, installPath);
        var installedExecutable = Path.Combine(installPath, relativeExecutable);
        logger.LogInformation("VideoSubFinder CLI installed at {Path}.", installedExecutable);
        return installedExecutable;
    }

    private static bool TryGetAssetNames(out string assetName, out IReadOnlyList<string> executableNames)
    {
        if (OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64)
        {
            assetName = "VideoSubFinder_6.10_x64.zip";
            // 官方包内为 VideoSubFinderWXW.exe（支持无头命令行）；若未来含 VideoSubFinderCli.exe 则优先命中。
            executableNames = ["VideoSubFinderCli.exe", "VideoSubFinderWXW.exe"];
            return true;
        }

        if (OperatingSystem.IsLinux() && RuntimeInformation.ProcessArchitecture == Architecture.X64)
        {
            assetName = "videosubfinder-cli-cpu-static-linux-x64.tar.gz";
            executableNames = ["VideoSubFinderCli.run", "VideoSubFinderCli"];
            return true;
        }

        if (OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.X64)
        {
            assetName = "videosubfinder-cli-darwin-x64.tar.gz";
            executableNames = ["VideoSubFinderCli.run", "VideoSubFinderCli"];
            return true;
        }

        assetName = string.Empty;
        executableNames = [];
        return false;
    }

    private static IReadOnlyList<string> GetExecutableNames() => OperatingSystem.IsWindows()
        ? ["VideoSubFinderWXW_intel.exe", "VideoSubFinderWXW.exe", "VideoSubFinderCli.exe"]
        : ["VideoSubFinderCli.run", "VideoSubFinderCli"];

    /// <summary>Windows 源 1：GitHub autovsf 镜像（含 SHA-256 digest）。</summary>
    private async Task<ReleaseArchive> GetWindowsGitHubReleaseAsync(CancellationToken cancellationToken)
        => await GetGitHubReleaseAsync(WindowsGitHubReleaseApi, "VideoSubFinder_6.10_x64.zip", cancellationToken);

    /// <summary>Windows 源 2：SourceForge 镜像直链（twds.dl，用户指定；SHA-256 已知）。</summary>
    private static Task<ReleaseArchive> GetWindowsSourceForgeDirectAsync(CancellationToken cancellationToken)
        => Task.FromResult(new ReleaseArchive(WindowsSourceForgeDirectZip, WindowsSourceForgeZipSha256));

    /// <summary>按序尝试各源：元数据解析 → 下载 → SHA-256 → 解压安装；全部失败返回 null。</summary>
    private async Task<string?> TryInstallFromSourcesAsync(
        string assetName, IReadOnlyList<string> executableNames, CancellationToken cancellationToken)
    {
        var sources = new List<Func<CancellationToken, Task<ReleaseArchive>>>();
        if (OperatingSystem.IsWindows())
        {
            sources.Add(ct => GetWindowsGitHubReleaseAsync(ct));
            sources.Add(ct => GetWindowsSourceForgeDirectAsync(ct));
            sources.Add(ct => GetWindowsSourceForgeReleaseAsync(ct));
        }
        else
        {
            sources.Add(ct => GetGitHubReleaseAsync(LinuxMacGitHubReleaseApi, assetName, ct));
        }

        foreach (var source in sources)
        {
            try
            {
                var release = await source(cancellationToken);
                if (await WasRejectedReleaseAsync(release, cancellationToken))
                {
                    logger.LogInformation("The current VideoSubFinder release was already checked and did not contain a usable executable.");
                    continue;
                }
                var installed = await DownloadAndInstallAsync(release, assetName, executableNames, cancellationToken);
                if (installed is not null)
                    return installed;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or IOException or TaskCanceledException)
            {
                logger.LogWarning(ex, "VideoSubFinder source failed; trying next download source.");
            }
        }

        return null;
    }

    /// <summary>SourceForge 官方最佳版本（无 SHA-256，仅 URL；部分网络不可达）。</summary>
    private async Task<ReleaseArchive> GetWindowsSourceForgeReleaseAsync(CancellationToken cancellationToken)
    {
        using var response = await HttpClient.GetAsync(SourceForgeReleaseApi, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var release = document.RootElement.GetProperty("release");
        var url = release.GetProperty("url").GetString();
        var hash = release.GetProperty("sha256sum").GetString();
        var sourceFileName = release.GetProperty("filename").GetString();
        var fileName = sourceFileName is null ? null : Path.GetFileName(sourceFileName);
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(hash) ||
            !fileName?.StartsWith("VideoSubFinder_", StringComparison.OrdinalIgnoreCase) == true)
            throw new InvalidDataException("SourceForge did not return the expected Windows VideoSubFinder package.");

        return new ReleaseArchive(RequireHttpsUrl(url), hash);
    }

    private static async Task<ReleaseArchive> GetGitHubReleaseAsync(string apiUrl, string assetName, CancellationToken cancellationToken)
    {
        using var response = await HttpClient.GetAsync(apiUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var assets = document.RootElement.GetProperty("assets");
        foreach (var asset in assets.EnumerateArray())
        {
            if (!asset.GetProperty("name").GetString()!.Equals(assetName, StringComparison.Ordinal))
                continue;

            var url = asset.GetProperty("browser_download_url").GetString();
            var digest = asset.TryGetProperty("digest", out var digestElement) ? digestElement.GetString() : null;
            var hash = digest?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true
                ? digest[7..]
                : null;
            return new ReleaseArchive(RequireHttpsUrl(url), hash);
        }

        throw new InvalidDataException($"GitHub release does not contain '{assetName}'.");
    }

    private static async Task DownloadAsync(string url, string destination, CancellationToken cancellationToken)
    {
        var candidates = Centurion.Core.Utils.Infrastructure.GitHubDownloadProxy.CandidateUrls(url).ToList();
        Exception? lastError = null;
        foreach (var candidate in candidates)
        {
            try
            {
                using var response = await HttpClient.GetAsync(candidate, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var output = File.Create(destination);
                await input.CopyToAsync(output, cancellationToken);
                if (new FileInfo(destination).Length == 0)
                    throw new IOException("Downloaded archive is empty.");
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException)
            {
                lastError = ex;
                try
                {
                    if (File.Exists(destination))
                        File.Delete(destination);
                }
                catch (IOException)
                {
                    // 忽略清理失败，下个候选覆盖写入
                }
            }
        }

        throw new IOException("Unable to download the VideoSubFinder archive.", lastError);
    }

    private static async Task VerifySha256Async(string path, string expectedHash, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
        if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"VideoSubFinder archive SHA-256 mismatch: expected {expectedHash}, got {actualHash}.");
    }

    private async Task<bool> WasRejectedReleaseAsync(ReleaseArchive release, CancellationToken cancellationToken)
    {
        if (!File.Exists(RejectedReleasePath))
            return false;

        var rejectedFingerprint = await File.ReadAllTextAsync(RejectedReleasePath, cancellationToken);
        return rejectedFingerprint.Equals(GetReleaseFingerprint(release), StringComparison.Ordinal);
    }

    private static string RejectedReleasePath => Path.Combine(ToolsRoot, ".unsupported-release");

    private static string GetReleaseFingerprint(ReleaseArchive release) => release.Sha256 ?? release.Url;

    private static string RequireHttpsUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("VideoSubFinder release metadata contained a non-HTTPS download URL.");
        return uri.AbsoluteUri;
    }

    private static void ExtractArchive(string archivePath, string destinationDirectory)
    {
        var root = Path.GetFullPath(destinationDirectory);
        var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        using var stream = File.OpenRead(archivePath);
        using var archive = ArchiveFactory.OpenArchive(stream);
        foreach (var entry in archive.Entries)
        {
            if (entry.IsDirectory || entry.Key is null)
                continue;

            var fullPath = Path.GetFullPath(Path.Combine(root, entry.Key));
            if (!fullPath.StartsWith(rootPrefix, pathComparison))
                throw new InvalidDataException($"Unsafe archive entry path rejected: {entry.Key}");

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException());
            using var entryStream = entry.OpenEntryStream();
            using var fileStream = File.Create(fullPath);
            entryStream.CopyTo(fileStream);
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Centurion/1.0");
        return client;
    }

    private sealed record ReleaseArchive(string Url, string? Sha256);
}
