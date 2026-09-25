using Centurion.Abstractions.Exceptions;
using Centurion.Abstractions.Utils;
using Centurion.Abstractions;
using Centurion.Core.Utils;
using Centurion.Models.Metadata;
using Microsoft.Extensions.Logging;
using SharpCompress.Archives;

namespace Centurion.Core.Managers;

/// <summary>
/// llama.cpp（llama-tts）管理器：确保 llama-tts.exe 可用。
/// 缺失时按设备变体（cpu/cuda/vulkan）从 metadata.json 注册表自动下载官方 zip，
/// 解压并扁平化到 tools/llama/。下载走 GitHub 520 镜像链，结果按进程缓存。
/// </summary>
public sealed class LlamaTtsManager(
    ToolRegistry registry,
    IBinaryLocator binaryLocator,
    ITempDirectoryManager tempManager,
    ILogger<LlamaTtsManager> logger)
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private string? _resolvedDirectory;

    /// <summary>llama-tts 是否已可用。</summary>
    public bool IsInstalled => LocateExecutable() is not null;

    /// <summary>
    /// 确保 llama-tts 已安装，返回可执行文件路径；失败返回 null。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<string?> EnsureInstalledAsync(CancellationToken cancellationToken)
    {
        var existing = LocateExecutable();
        if (existing is not null)
            return existing;

        await Gate.WaitAsync(cancellationToken);
        try
        {
            existing = LocateExecutable();
            if (existing is not null)
                return existing;

            if (!registry.Tools.TryGetValue("llama", out var meta))
            {
                logger.LogWarning("llama.cpp is not registered in metadata.json; TTS auto-download unavailable.");
                return null;
            }

            var toolsRoot = Path.Combine(AppContext.BaseDirectory, "tools", "llama");
            Directory.CreateDirectory(toolsRoot);

            await using var tempDir = await tempManager.CreateTempDirectoryAsync("llama_");
            var archivePath = Path.Combine(tempDir.Path, "llama.zip");
            var extractDir = Path.Combine(tempDir.Path, "extract");

            logger.LogInformation("llama-tts not found. Downloading llama.cpp {Version} from {Url} ...", meta.Version, meta.DownloadUrl);
            await DownloadAsync(meta.DownloadUrl!, archivePath, cancellationToken);

            Directory.CreateDirectory(extractDir);
            ExtractArchive(archivePath, extractDir);

            var exe = Directory.GetFiles(extractDir, "llama-tts.exe", SearchOption.AllDirectories).FirstOrDefault()
                      ?? Directory.GetFiles(extractDir, "llama-tts", SearchOption.AllDirectories).FirstOrDefault();
            if (exe is null)
            {
                logger.LogWarning("llama-tts.exe not found inside the llama.cpp archive; installation failed. "
                                  + "Note: llama-tts requires a recent llama.cpp build (Qwen3-TTS merged Aug 2026).");
                return null;
            }
            Flatten(extractDir, exe, toolsRoot);

            var relocated = LocateExecutable();
            if (relocated is null)
            {
                logger.LogWarning("llama.cpp extracted but llama-tts.exe still cannot be located.");
                return null;
            }

            _resolvedDirectory = Path.GetDirectoryName(relocated);
            logger.LogInformation("llama.cpp installed successfully at {Directory}.", _resolvedDirectory);
            return relocated;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>定位 llama-tts.exe（本地 tools/llama + PATH）。</summary>
    private string? LocateExecutable()
    {
        if (_resolvedDirectory is { } cached && File.Exists(Path.Combine(cached, "llama-tts.exe")))
            return Path.Combine(cached, "llama-tts.exe");

        try
        {
            return binaryLocator.Locate(OperatingSystem.IsWindows() ? "llama-tts.exe" : "llama-tts", "tools/llama");
        }
        catch (BinaryNotFoundException)
        {
            return null;
        }
    }

    private static async Task DownloadAsync(string url, string destination, CancellationToken cancellationToken)
    {
        // llama.cpp 在 GitHub：走 520 镜像链（GitHubDownloadProxy 内部逐个候选尝试）
        await GitHubDownloadProxy.DownloadWithFallbackAsync(
            url,
            candidate => DownloadFileAsync(candidate, destination, cancellationToken),
            ex => ex is HttpRequestException or TaskCanceledException or IOException,
            reason => Console.WriteLine($"[llama] GitHub mirror fallback: {reason}"),
            cancellationToken);
    }

    private static async Task DownloadFileAsync(string url, string destination, CancellationToken cancellationToken)
    {
        // 镜像候选通常不可达：短超时快速失败，让 GitHubDownloadProxy 尽快切到下一个候选（最终直连）
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Centurion/1.0");
        var bytes = await client.GetByteArrayAsync(url, cancellationToken);
        await File.WriteAllBytesAsync(destination, bytes, cancellationToken);
    }

    /// <summary>解压 zip/7z 归档到目标目录，防 zip-slip。</summary>
    private static void ExtractArchive(string archivePath, string destinationDirectory)
    {
        var root = Path.GetFullPath(destinationDirectory);
        using var stream = File.OpenRead(archivePath);
        using var archive = ArchiveFactory.OpenArchive(stream);
        foreach (var entry in archive.Entries)
        {
            if (entry.IsDirectory || entry.Key is null)
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

    /// <summary>将包含 llama-tts.exe 的子目录内容扁平化移动到工具根目录，删除空目录。</summary>
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
                    // 瞬时占用：保留目录不影响功能
                }
            }
        }
    }
}
