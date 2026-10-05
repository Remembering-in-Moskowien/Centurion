using Centurion.Models.Workflow;
using Centurion.Abstractions.Utils;

using Centurion.Abstractions;
using Centurion.Models.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpCompress.Archives;
using Centurion.Core.Operators.Download;
using Centurion.Core.Operators.Download.Request;
namespace Centurion.Core.Capabilities.Managers.Tools;

/// <summary>
/// Manages downloading, extracting, and paths for external tools (ASR engines).
/// Supports automatically selecting the matching tool variant for the inference device
/// (CUDA/Vulkan/CPU, etc.) when downloading.
/// </summary>
public class ToolManager : IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ToolManager> _logger;
    private readonly string _toolsRoot;
    private readonly ToolMeta _toolMeta;

    /// <summary>Root directory where the tool is extracted.</summary>
    public string ToolDirectory { get; private set; }
    /// <summary>Full path to the tool's main executable.</summary>
    public string ExecutablePath { get; private set; }

    /// <summary>Base URL for downloading the tool's runtime model/weights (may be null; when null, the tool's built-in default is used).</summary>
    public string? ModelBaseUrl { get; }

    /// <summary>The device variant actually selected (null means the base build).</summary>
    public string? ActiveVariantDescription { get; }

    /// <summary>
    /// Resolves the matching download variant from the registry based on the tool name and target
    /// inference device, and initializes the paths.
    /// </summary>
    /// <param name="toolName">Name of the tool to manage.</param>
    /// <param name="registry">Registry containing metadata for all available tools.</param>
    /// <param name="device">Target inference device, used to select the CUDA/Vulkan/DirectML/CPU variant.</param>
    /// <param name="serviceProvider">Service provider, used to resolve dependencies such as logging.</param>
    public ToolManager(string toolName, ToolRegistry registry, InferenceDevice device, IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = serviceProvider.GetRequiredService<ILogger<ToolManager>>();
        _toolsRoot = Path.Combine(AppContext.BaseDirectory, "tools", "asr");
        Directory.CreateDirectory(_toolsRoot);

        if (!registry.Tools.TryGetValue(toolName, out var baseMeta))
            throw new ArgumentException($"Unsupported tool: {toolName}", nameof(toolName));

        // Pick the variant by device (exact match -> default -> base fields)
        var (url, archiveType, exeRelative, description, fileHash) = ResolveVariant(baseMeta, device);
        _toolMeta = new ToolMeta
        {
            ToolName = baseMeta.ToolName,
            DownloadUrl = url,
            ArchiveType = archiveType,
            ExecutableRelativePath = exeRelative,
            Version = baseMeta.Version,
            ModelBaseUrl = baseMeta.ModelBaseUrl,
            FileHash = fileHash
        };
        ModelBaseUrl = baseMeta.ModelBaseUrl;
        ActiveVariantDescription = description;

        ToolDirectory = Path.Combine(_toolsRoot, toolName);
        ExecutablePath = Path.Combine(ToolDirectory, _toolMeta.ExecutableRelativePath);
    }

    /// <summary>
    /// Resolves the download information the tool should use for the given device (internal, to
    /// facilitate unit testing).
    /// Match order: device key (cuda/vulkan/directml/cpu) -> "default" -> base fields.
    /// </summary>
    internal static (string Url, string ArchiveType, string ExecutableRelativePath, string? Description, string? FileHash) ResolveVariant(
        ToolMeta meta, InferenceDevice device)
    {
        var variants = meta.Variants;
        if (variants is null || variants.Count == 0)
            return (meta.DownloadUrl, meta.ArchiveType, meta.ExecutableRelativePath, null, meta.FileHash);

        var deviceKey = device switch
        {
            InferenceDevice.Cuda => "cuda",
            InferenceDevice.Vulkan => "vulkan",
            InferenceDevice.DirectMl => "directml",
            InferenceDevice.Cpu => "cpu",
            _ => "cpu"
        };

        ToolVariant? variant = null;
        if (variants.TryGetValue(deviceKey, out var exact))
            variant = exact;
        else if (variants.TryGetValue("default", out var fallback))
            variant = fallback;

        if (variant is null)
            return (meta.DownloadUrl, meta.ArchiveType, meta.ExecutableRelativePath, null, meta.FileHash);

        return (
            variant.DownloadUrl ?? meta.DownloadUrl,
            variant.ArchiveType ?? meta.ArchiveType,
            variant.ExecutableRelativePath ?? meta.ExecutableRelativePath,
            variant.Description,
            variant.FileHash ?? meta.FileHash);
    }

    /// <summary>
    /// Ensures the tool is downloaded and extracted; downloads it automatically if missing.
    /// </summary>
    public async Task EnsureToolAsync(CancellationToken cancellationToken = default)
    {
        if (ActiveVariantDescription is not null)
            _logger.LogInformation("Tool '{ToolName}' selected {Variant} build.", _toolMeta.ToolName, ActiveVariantDescription);

        if (File.Exists(ExecutablePath))
        {
            _logger.LogInformation("Tool '{ToolName}' already exists at {Path}", _toolMeta.ToolName, ExecutablePath);
            return;
        }

        _logger.LogInformation("Tool '{ToolName}' not found. Downloading...", _toolMeta.ToolName);
        await DownloadAndExtractAsync(cancellationToken);

        // After extraction, nested directories may mean ExecutablePath does not exist; flatten the layout.
        await NormalizeToolDirectoryAsync(cancellationToken);
    }

    private async Task DownloadAndExtractAsync(CancellationToken cancellationToken)
    {
        // Download temp files go into the temp directory under the application root, cleaned up
        // automatically with the handle.
        await using var tempDir = await _serviceProvider.GetRequiredService<ITempDirectoryManager>().CreateTempDirectoryAsync("tool_");
        var tempFile = Path.Combine(tempDir.Path, "download_archive");
        try
        {
            // 1. Download
            using var downloader = _serviceProvider.GetRequiredService<Centurion.Core.Operators.Download.Downloader>();
            var request = new OperatorsRequest<AriaDownloadRequest>
            {
                Payload = new AriaDownloadRequest
                {
                    Url = _toolMeta.DownloadUrl,
                    FullSavePath = tempFile,
                    FileHash = _toolMeta.FileHash ?? string.Empty,
                    SplitThread = 8,
                    ServerConnection = 8,
                    MaxRetry = 5,
                    ProgressRefreshMs = 200
                }
            };
            await downloader.ProcessAsync(request, cancellationToken);

            // 2. Extract
            Directory.CreateDirectory(ToolDirectory);
            _logger.LogInformation("Extracting {ArchiveType} archive to {ToolDirectory}", _toolMeta.ArchiveType, ToolDirectory);

            if (_toolMeta.ArchiveType.Equals("zip", StringComparison.OrdinalIgnoreCase))
            {
                System.IO.Compression.ZipFile.ExtractToDirectory(tempFile, ToolDirectory, true);
            }
            else if (_toolMeta.ArchiveType.Equals("tar.gz", StringComparison.OrdinalIgnoreCase) ||
                     _toolMeta.ArchiveType.Equals("tgz", StringComparison.OrdinalIgnoreCase))
            {
                using var stream = File.OpenRead(tempFile);
                using var reader = ArchiveFactory.OpenArchive(stream);
                var root = Path.GetFullPath(ToolDirectory);
                foreach (var entry in reader.Entries)
                {
                    if (entry.IsDirectory)
                        continue;
                    if (entry.Key == null) continue;

                    // Guard against zip-slip: reject any entry path that escapes the tool directory.
                    var fullPath = Path.GetFullPath(Path.Combine(root, entry.Key));
                    if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                        throw new InvalidDataException($"Unsafe archive entry path rejected: {entry.Key}");

                    Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException());
                    using var entryStream = entry.OpenEntryStream();
                    using var fileStream = File.Create(fullPath);
                    await entryStream.CopyToAsync(fileStream, cancellationToken);
                }
            }
            else
            {
                throw new NotSupportedException($"Archive type '{_toolMeta.ArchiveType}' is not supported.");
            }

            _logger.LogInformation("Tool '{ToolName}' installed successfully at {ExecutablePath}", _toolMeta.ToolName, ExecutablePath);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    /// <summary>
    /// Flattens the tool directory: if ExecutablePath does not exist, searches the subdirectories
    /// for the executable, moves everything in its directory to the root, and removes empty
    /// directories.
    /// </summary>
    private async Task NormalizeToolDirectoryAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(ExecutablePath))
            return;

        var exeName = Path.GetFileName(ExecutablePath);
        _logger.LogWarning("Executable '{ExeName}' not found at expected path. Searching in subdirectories...", exeName);

        // Recursively search for a file with the executable's name
        var foundFiles = Directory.GetFiles(ToolDirectory, exeName, SearchOption.AllDirectories);
        if (foundFiles.Length == 0)
        {
            _logger.LogWarning("Executable '{ExeName}' not found anywhere in {ToolDirectory}. Tool may be broken.", exeName, ToolDirectory);
            return;
        }

        var firstMatch = foundFiles[0];
        var sourceDir = Path.GetDirectoryName(firstMatch)!;
        if (string.Equals(sourceDir, ToolDirectory, StringComparison.OrdinalIgnoreCase))
        {
            // Already in the root, but the path may differ in casing; update it.
            ExecutablePath = firstMatch;
            _logger.LogInformation("Executable found at {Path}", ExecutablePath);
            return;
        }

        // Move everything under sourceDir into the ToolDirectory root.
        _logger.LogInformation("Flattening directory: moving contents from {SourceDir} to {ToolDirectory}", sourceDir, ToolDirectory);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var dest = Path.Combine(ToolDirectory, Path.GetFileName(file));
            if (File.Exists(dest))
                File.Delete(dest);
            File.Move(file, dest);
        }
        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var dest = Path.Combine(ToolDirectory, Path.GetFileName(dir));
            if (Directory.Exists(dest))
                Directory.Delete(dest, true);
            Directory.Move(dir, dest);
        }

        // Delete the now-empty original directory (and any empty parents, but only up to the root).
        DeleteEmptySubdirectories(ToolDirectory);

        // Update ExecutablePath.
        var newExePath = Path.Combine(ToolDirectory, exeName);
        if (File.Exists(newExePath))
            ExecutablePath = newExePath;
        else
        {
            // Search again (it may have been moved elsewhere, but in theory it is now in the root).
            var newFound = Directory.GetFiles(ToolDirectory, exeName, SearchOption.TopDirectoryOnly);
            if (newFound.Length > 0)
                ExecutablePath = newFound[0];
        }

        _logger.LogInformation("Normalized executable path to {ExecutablePath}", ExecutablePath);
        await Task.CompletedTask; // Keep the async signature consistent.
    }

    /// <summary>
    /// Recursively deletes all empty subdirectories (without deleting the root).
    /// </summary>
    private void DeleteEmptySubdirectories(string root)
    {
        foreach (var dir in Directory.GetDirectories(root))
        {
            DeleteEmptySubdirectories(dir);
            if (!Directory.EnumerateFileSystemEntries(dir).Any())
            {
                try
                {
                    Directory.Delete(dir, false);
                    _logger.LogDebug("Deleted empty directory: {Dir}", dir);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not delete empty directory {Dir}", dir);
                }
            }
        }
    }

    /// <summary>
    /// Releases resources; this manager holds no unmanaged resources to release.
    /// </summary>
    public void Dispose() { }
}