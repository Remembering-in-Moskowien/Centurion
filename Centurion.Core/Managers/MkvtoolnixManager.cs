using Centurion.Abstractions;
using Centurion.Abstractions.Exceptions;
using Centurion.Abstractions.Utils;
using Centurion.Models.Metadata;
using Microsoft.Extensions.Logging;
using SharpCompress.Archives;

namespace Centurion.Core.Managers;

/// <summary>
/// MKVToolNix 管理器：确保 mkvmerge / mkvextract 可用。
/// 本机或 PATH 中缺失时，按 metadata.json 注册表自动下载官方便携 7z 包，
/// 解压并扁平化到 tools/mkvtoolnix/。下载为一次性动作，结果按进程缓存。
/// </summary>
public sealed class MkvtoolnixManager(
    ToolRegistry registry,
    IBinaryLocator binaryLocator,
    ITempDirectoryManager tempManager,
    ILogger<MkvtoolnixManager> logger)
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private string? _resolvedDirectory;

    /// <summary>mkvtoolnix 是否已可用（mkvmerge.exe 在本机或 PATH 中）。</summary>
    public bool IsInstalled => LocateExecutable() is not null;

    /// <summary>
    /// 确保 mkvtoolnix 已安装：已存在则直接返回工具目录；
    /// 缺失时自动下载官方 7z 并解压，返回工具目录；失败返回 null。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<string?> EnsureInstalledAsync(CancellationToken cancellationToken)
    {
        var existing = LocateExecutable();
        if (existing is not null)
            return Path.GetDirectoryName(existing);

        await Gate.WaitAsync(cancellationToken);
        try
        {
            existing = LocateExecutable();
            if (existing is not null)
                return Path.GetDirectoryName(existing);

            if (!registry.Tools.TryGetValue("mkvtoolnix", out var meta))
            {
                logger.LogWarning("MKVToolNix is not registered in metadata.json; auto-download unavailable.");
                return null;
            }

            var toolsRoot = Path.Combine(AppContext.BaseDirectory, "tools", "mkvtoolnix");
            Directory.CreateDirectory(toolsRoot);

            await using var tempDir = await tempManager.CreateTempDirectoryAsync("mkvtoolnix_");
            var archivePath = Path.Combine(tempDir.Path, "mkvtoolnix.7z");
            var extractDir = Path.Combine(tempDir.Path, "extract");

            logger.LogInformation("MKVToolNix {Version} not found. Downloading from {Url} ...", meta.Version, meta.DownloadUrl);
            await DownloadAsync(meta.DownloadUrl, archivePath, cancellationToken);

            Directory.CreateDirectory(extractDir);
            ExtractArchive(archivePath, extractDir);

            var exe = Directory.GetFiles(extractDir, "mkvmerge.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (exe is null)
            {
                logger.LogWarning("mkvmerge.exe not found inside the MKVToolNix archive; installation failed.");
                return null;
            }
            Flatten(extractDir, exe, toolsRoot);

            var relocated = LocateExecutable();
            if (relocated is null)
            {
                logger.LogWarning("MKVToolNix extracted but mkvmerge.exe still cannot be located.");
                return null;
            }

            _resolvedDirectory = Path.GetDirectoryName(relocated);
            logger.LogInformation("MKVToolNix installed successfully at {Directory}.", _resolvedDirectory);
            return _resolvedDirectory;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>定位 mkvmerge.exe（本地 tools/mkvtoolnix + PATH）。</summary>
    private string? LocateExecutable()
    {
        if (_resolvedDirectory is { } cached && File.Exists(Path.Combine(cached, "mkvmerge.exe")))
            return Path.Combine(cached, "mkvmerge.exe");

        try
        {
            return binaryLocator.Locate(OperatingSystem.IsWindows() ? "mkvmerge.exe" : "mkvmerge", "tools/mkvtoolnix");
        }
        catch (BinaryNotFoundException)
        {
            return null;
        }
    }

    private static async Task DownloadAsync(string url, string destination, CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Centurion/1.0");
        var bytes = await client.GetByteArrayAsync(url, cancellationToken);
        await File.WriteAllBytesAsync(destination, bytes, cancellationToken);
    }

    /// <summary>
    /// 解压 7z/zip 归档到目标目录，防 zip-slip（条目路径逃逸拒绝）。
    /// </summary>
    private static void ExtractArchive(string archivePath, string destinationDirectory)
    {
        var root = Path.GetFullPath(destinationDirectory);
        using var stream = File.OpenRead(archivePath);
        using var archive = ArchiveFactory.OpenArchive(stream);
        foreach (var entry in archive.Entries)
        {
            if (entry.IsDirectory)
                continue;
            if (entry.Key is null)
                continue;

            var fullPath = Path.GetFullPath(Path.Combine(root, entry.Key));
            if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new InvalidDataException($"Unsafe archive entry path rejected: {entry.Key}");

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException());
            using var entryStream = entry.OpenEntryStream();
            using var fileStream = File.Create(fullPath);
            entryStream.CopyTo(fileStream);
        }
    }

    /// <summary>
    /// 将包含 mkvmerge.exe 的子目录内容扁平化移动到工具根目录，删除空目录。
    /// </summary>
    private static void Flatten(string extractDir, string executablePath, string toolsRoot)
    {
        var sourceDir = Path.GetDirectoryName(executablePath)!;
        if (string.Equals(Path.GetFullPath(sourceDir), Path.GetFullPath(toolsRoot), StringComparison.OrdinalIgnoreCase))
            return;

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var dest = Path.Combine(toolsRoot, Path.GetFileName(file));
            if (File.Exists(dest))
                File.Delete(dest);
            File.Move(file, dest);
        }
        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var dest = Path.Combine(toolsRoot, Path.GetFileName(dir));
            if (Directory.Exists(dest))
                Directory.Delete(dest, true);
            Directory.Move(dir, dest);
        }

        DeleteEmptySubdirectories(extractDir);
    }

    private static void DeleteEmptySubdirectories(string root)
    {
        foreach (var dir in Directory.GetDirectories(root))
        {
            DeleteEmptySubdirectories(dir);
            if (!Directory.EnumerateFileSystemEntries(dir).Any())
            {
                try
                {
                    Directory.Delete(dir, false);
                }
                catch (IOException)
                {
                    // 文件占用等瞬时问题：保留目录不影响功能
                }
            }
        }
    }
}
