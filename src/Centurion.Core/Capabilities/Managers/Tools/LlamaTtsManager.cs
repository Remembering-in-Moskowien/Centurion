using Centurion.Abstractions.Exceptions;
using Centurion.Abstractions.Utils;
using Centurion.Abstractions;
using Centurion.Models.Metadata;
using Microsoft.Extensions.Logging;
using SharpCompress.Archives;
using Centurion.Core.Utils.Infrastructure;
namespace Centurion.Core.Capabilities.Managers.Tools;

/// <summary>
/// llama.cpp (llama-tts) manager: ensures llama-tts.exe is available.
/// When missing, automatically downloads the official zip from the metadata.json registry per the
/// device variant (cpu/cuda/vulkan), extracts and flattens it into tools/llama/. Downloads go
/// through the GitHub 520 mirror chain; the result is cached per process.
/// </summary>
public sealed class LlamaTtsManager(
    ToolRegistry registry,
    IBinaryLocator binaryLocator,
    ITempDirectoryManager tempManager,
    ILogger<LlamaTtsManager> logger)
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private string? _resolvedDirectory;

    /// <summary>Whether llama-tts is available.</summary>
    public bool IsInstalled => LocateExecutable() is not null;

    /// <summary>
    /// Ensures llama-tts is installed and returns the executable path; returns null on failure.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
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
                logger.LogWarning(
                    "llama.cpp is not registered in metadata.json; TTS auto-download unavailable. "
                    + "Use the self-contained QORA engine instead: dub --tts-engine qora (auto-downloads).");
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

    /// <summary>Locates llama-tts.exe (local tools/llama + PATH).</summary>
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

    private async Task DownloadAsync(string url, string destination, CancellationToken cancellationToken)
    {
        // llama.cpp is hosted on GitHub: go through the 520 mirror chain
        // (GitHubDownloadProxy tries each candidate in turn internally).
        await GitHubDownloadProxy.DownloadWithFallbackAsync(
            url,
            candidate => DownloadFileAsync(candidate, destination, cancellationToken),
            ex => ex is HttpRequestException or TaskCanceledException or IOException,
            reason => logger.LogInformation("[llama] GitHub mirror fallback: {Reason}", reason),
            cancellationToken);
    }

    private static async Task DownloadFileAsync(string url, string destination, CancellationToken cancellationToken)
    {
        // Mirror candidates are usually unreachable: fail fast with a short timeout so
        // GitHubDownloadProxy moves on to the next candidate quickly (ultimately a direct connection).
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Centurion/1.0");
        var bytes = await client.GetByteArrayAsync(url, cancellationToken);
        await File.WriteAllBytesAsync(destination, bytes, cancellationToken);
    }

    /// <summary>Extracts a zip/7z archive into the destination directory, guarding against zip-slip.</summary>
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

    /// <summary>Flattens the contents of the subdirectory containing llama-tts.exe into the tool root directory, removing empty directories.</summary>
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
                    // Transient lock: leaving the directory in place does not affect functionality.
                }
            }
        }
    }
}
