using Centurion.Abstractions;
using Centurion.Abstractions.Exceptions;
using Centurion.Abstractions.Utils;
using Centurion.Models.Metadata;
using Microsoft.Extensions.Logging;
using SharpCompress.Archives;

namespace Centurion.Core.Capabilities.Managers.Media;

/// <summary>
/// MKVToolNix manager: ensures mkvmerge / mkvextract is available.
/// When missing locally or on PATH, automatically downloads the official portable 7z package
/// per the metadata.json registry, extracts and flattens it into tools/mkvtoolnix/.
/// The download is a one-time action; the result is cached per process.
/// </summary>
public sealed class MkvtoolnixManager(
    ToolRegistry registry,
    IBinaryLocator binaryLocator,
    ITempDirectoryManager tempManager,
    ILogger<MkvtoolnixManager> logger)
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private string? _resolvedDirectory;

    /// <summary>Whether mkvtoolnix is available (mkvmerge.exe present locally or on PATH).</summary>
    public bool IsInstalled => LocateExecutable() is not null;

    /// <summary>
    /// Ensures mkvtoolnix is installed: if already present, returns the tool directory directly;
    /// if missing, automatically downloads and extracts the official 7z and returns the tool
    /// directory; returns null on failure.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
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

    /// <summary>Locates mkvmerge.exe (local tools/mkvtoolnix + PATH).</summary>
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
    /// Extracts a 7z/zip archive into the destination directory, guarding against zip-slip
    /// (rejects entries whose path escapes the target).
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
    /// Flattens the contents of the subdirectory containing mkvmerge.exe into the tool root
    /// directory, removing empty directories.
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
                    // Transient issues such as file locks: leaving the directory in place does not affect functionality.
                }
            }
        }
    }
}
