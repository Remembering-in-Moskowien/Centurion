using System.Text.Json;
using Centurion.Abstractions;
using Centurion.Core.Capabilities.Infrastructure;
using Centurion.Core.Infrastructure;
using Centurion.Core.Operators.Download;
using Centurion.Core.Operators.Download.Request;
using Centurion.Models.Console;
using Centurion.Models.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace Centurion.Core.Capabilities.Managers.Media;

/// <summary>
/// Model manager responsible for downloading, verifying, and managing paths for model files.
/// Supports both single-file and directory models.
/// </summary>
/// <remarks>
/// Models are stored content-addressed: every downloaded model file lands under its own SHA-256
/// (lowercase hex) — <c>models/{category}/{sha256}.{ext}</c> for single-file models, and
/// <c>models/{category}/{aggregateSha256}/</c> (member files keep their original names) for
/// directory models, where the aggregate hash covers each member's relative path and content hash.
/// A per-category manifest (<c>models/{category}/.manifest.json</c>) maps the logical model name
/// to its content hash so paths can be resolved without rescanning or remembering hashes.
/// </remarks>
public class ModelManager : IDisposable
{
    private const string ManifestFileName = ".manifest.json";
    private const string DownloadPrefix = ".download-";

    private readonly IServiceProvider _serviceProvider;
    private readonly string _modelName;
    private readonly ModelMeta _targetMeta;

    /// <summary>Path to the model file; a file path in single-file mode, a directory path in directory mode. Populated after install/health resolution.</summary>
    public string ModelFilePath { get; private set; } = string.Empty;
    /// <summary>Path to the folder containing the model; for directory models this is the content-hash subdirectory once installed.</summary>
    public string ModelFolder { get; private set; }
    /// <summary>Metadata for the current model; <see langword="null"/> when management is disabled.</summary>
    public ModelMeta? TargetMeta => _targetMeta;
    /// <summary>Whether model management is enabled (<see langword="false"/> when the model name is empty).</summary>
    public bool ManagementEnabled { get; }
    /// <summary>Content hash of the installed model (file hash or aggregate directory hash), empty until resolved.</summary>
    public string InstalledHash { get; private set; } = string.Empty;
    /// <summary>Category directory name (e.g. whisper/diarization/vad).</summary>
    public string CategoryFolder { get; }

    /// <summary>
    /// Constructor.
    /// </summary>
    /// <param name="modelName">Model name.</param>
    /// <param name="modelDict">Model metadata dictionary.</param>
    /// <param name="serviceProvider">Service provider.</param>
    /// <param name="categoryFolder">Category folder name for the model (e.g. whisper/diarization/vad).</param>
    /// <param name="modelsRoot">Optional models root directory (defaults to <see cref="AppContext.BaseDirectory"/>); models land under modelsRoot/models/{categoryFolder}/.</param>
    public ModelManager(string modelName,
        IReadOnlyDictionary<string, ModelMeta> modelDict,
        IServiceProvider serviceProvider,
        string categoryFolder = "common",
        string? modelsRoot = null)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _modelName = string.Empty;
        _targetMeta = null!;
        ModelFolder = string.Empty;
        CategoryFolder = categoryFolder;

        if (string.IsNullOrEmpty(modelName))
        {
            ManagementEnabled = false;
            return;
        }

        ManagementEnabled = true;
        _modelName = modelName.Trim().ToLowerInvariant();
        if (!modelDict.TryGetValue(_modelName, out var tempMeta))
            throw new ArgumentException(
                $"Unsupported model: {_modelName}",
                nameof(modelName));

        _targetMeta = tempMeta;

        var root = Path.GetFullPath(modelsRoot ?? AppContext.BaseDirectory);
        ModelFolder = Path.Combine(root, "models", categoryFolder);
    }

    /// <summary>Path of the per-category manifest file.</summary>
    private string ManifestPath => Path.Combine(ModelFolder, ManifestFileName);

    /// <summary>
    /// Checks model integrity; throws <see cref="ModelMissingException"/> when files are missing
    /// (prompting the user to install via <c>Centurion models install &lt;model&gt;</c>), without
    /// any automatic download. Returns directly when management is disabled.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    public async Task CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        if (!ManagementEnabled) return;

        if (!await TryResolveInstalledAsync())
        {
            var hint = FindMissingHint();
            throw new ModelMissingException(_modelName, hint, [hint]);
        }
        await Task.CompletedTask;
    }

    /// <summary>
    /// Installs the model: downloads the required files when missing/incomplete (used by the
    /// models install command and on-demand by strategies), hashes the content and stores it under
    /// its SHA-256 name, then records the mapping in the category manifest.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    public async Task EnsureInstalledAsync(CancellationToken cancellationToken = default)
    {
        if (!ManagementEnabled) return;

        if (await TryResolveInstalledAsync())
            return;

        if (_targetMeta.DownloadType is ModelDownloadType.Directory or ModelDownloadType.OnnxModelDirectory)
            await DownloadDirectoryModelAsync(cancellationToken);
        else
            await DownloadModelAsync(cancellationToken);
    }

    /// <summary>
    /// Resolves <see cref="ModelFilePath"/>/<see cref="ModelFolder"/> from the category manifest when
    /// the model is already installed. Returns false when the model is not installed or incomplete.
    /// </summary>
    public async Task<bool> TryResolveInstalledAsync()
    {
        var manifest = await LoadManifestAsync();
        if (!manifest.TryGetValue(_modelName, out var entry))
            return false;

        if (entry.Kind == "dir")
        {
            var dir = Path.Combine(ModelFolder, entry.Hash);
            if (!Directory.Exists(dir) || !entry.Files!.All(f => File.Exists(Path.Combine(dir, f))))
                return false;
            ModelFolder = dir;
            ModelFilePath = dir;
            InstalledHash = entry.Hash;
            return true;
        }

        var filePath = Path.Combine(ModelFolder, entry.Hash + "." + entry.Ext);
        if (!File.Exists(filePath) || new FileInfo(filePath).Length == 0)
            return false;
        ModelFilePath = filePath;
        InstalledHash = entry.Hash;
        return true;
    }

    /// <summary>User-facing hint of what is expected locally (hash naming means the path is only known after install).</summary>
    private string FindMissingHint()
    {
        if (_targetMeta.DownloadType is ModelDownloadType.Directory or ModelDownloadType.OnnxModelDirectory)
            return $"models/{CategoryFolder}/<content-hash>/ (files: {string.Join(", ", _targetMeta.Files ?? [])})";
        var ext = Path.GetExtension(_targetMeta.FileName) ?? "";
        return $"models/{CategoryFolder}/<sha256>{ext}";
    }

    private async Task DownloadModelAsync(CancellationToken cancellationToken)
    {
        if (!ManagementEnabled) return;
        Directory.CreateDirectory(ModelFolder);
        ConsoleServices.Output.WriteInfo($"Model '{_modelName}' not found; downloading...");
        cancellationToken.ThrowIfCancellationRequested();

        var fileName = _targetMeta.FileName!;
        var ext = Path.GetExtension(fileName) ?? "";
        var tmpPath = Path.Combine(ModelFolder, $"{DownloadPrefix}{Guid.NewGuid():N}{ext}");

        using var aria = _serviceProvider.GetRequiredService<Centurion.Core.Operators.Download.Downloader>();
        var request = new OperatorsRequest<AriaDownloadRequest>
        {
            Payload = new AriaDownloadRequest
            {
                Url = _targetMeta.DownloadUrl!,
                FullSavePath = tmpPath,
                FileHash = _targetMeta.FileHash ?? string.Empty,
                SplitThread = 4,
                ServerConnection = 4,
                MaxRetry = 5,
                ProgressRefreshMs = 100
            }
        };

        try
        {
            await aria.ProcessAsync(request, cancellationToken);
            var hash = ContentHasher.ComputeFileSha256(tmpPath);
            var finalPath = Path.Combine(ModelFolder, hash + ext);
            if (File.Exists(finalPath))
                File.Delete(tmpPath); // content already present (deduplicated)
            else
                File.Move(tmpPath, finalPath);

            await WriteManifestEntryAsync(new ManifestEntry { Kind = "file", Hash = hash, Ext = ext, FileName = fileName });
            ModelFilePath = finalPath;
            InstalledHash = hash;
            ConsoleServices.Output.WriteLine($"Model '{_modelName}' downloaded successfully ({hash}).");
        }
        finally
        {
            TryDelete(tmpPath);
        }
    }

    private async Task DownloadDirectoryModelAsync(CancellationToken cancellationToken)
    {
        if (_targetMeta.Files == null || _targetMeta.Files.Count == 0)
            throw new InvalidOperationException("No files specified for directory model.");

        Directory.CreateDirectory(ModelFolder);
        var tmpDir = Path.Combine(ModelFolder, $"{DownloadPrefix}{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);

        try
        {
            using var aria = _serviceProvider.GetRequiredService<Centurion.Core.Operators.Download.Downloader>();
            var downloads = _targetMeta.Files.Select(async fileName =>
            {
                var fileUrl = _targetMeta.FileUrls != null && _targetMeta.FileUrls.TryGetValue(fileName, out var overrideUrl)
                    ? overrideUrl
                    : _targetMeta.DownloadUrl!.TrimEnd('/') + "/" + fileName;
                var savePath = Path.Combine(tmpDir, fileName);
                Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);
                var request = new OperatorsRequest<AriaDownloadRequest>
                {
                    Payload = new AriaDownloadRequest
                    {
                        Url = fileUrl,
                        FullSavePath = savePath,
                        FileHash = _targetMeta.FileHash ?? string.Empty
                    }
                };
                await aria.ProcessAsync(request, cancellationToken);
            });
            await Task.WhenAll(downloads);

            var members = _targetMeta.Files
                .Select(f => (RelativePath: f, FilePath: Path.Combine(tmpDir, f)))
                .ToList();
            var hash = ContentHasher.ComputeAggregateSha256(members);

            var finalDir = Path.Combine(ModelFolder, hash);
            if (Directory.Exists(finalDir))
                TryDeleteDirectory(tmpDir); // same content already installed (deduplicated)
            else
                Directory.Move(tmpDir, finalDir);

            await WriteManifestEntryAsync(new ManifestEntry
            {
                Kind = "dir",
                Hash = hash,
                Files = _targetMeta.Files,
                FileName = _targetMeta.Files.Count == 1 ? _targetMeta.Files[0] : null
            });
            ModelFolder = finalDir;
            ModelFilePath = finalDir;
            InstalledHash = hash;
            ConsoleServices.Output.WriteLine($"Model '{_modelName}' downloaded successfully ({hash}).");
        }
        finally
        {
            TryDeleteDirectory(tmpDir);
        }
    }

    // ---------- Manifest ----------

    private sealed class ManifestEntry
    {
        public string Kind { get; set; } = "file";
        public string Hash { get; set; } = "";
        public string? Ext { get; set; }
        public string? FileName { get; set; }
        public List<string>? Files { get; set; }
    }

    private async Task<Dictionary<string, ManifestEntry>> LoadManifestAsync()
    {
        if (!File.Exists(ManifestPath))
            return new Dictionary<string, ManifestEntry>(StringComparer.OrdinalIgnoreCase);
        try
        {
            await using var stream = File.OpenRead(ManifestPath);
            var doc = await JsonSerializer.DeserializeAsync<Dictionary<string, ManifestEntry>>(stream);
            return doc ?? new Dictionary<string, ManifestEntry>(StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new Dictionary<string, ManifestEntry>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task WriteManifestEntryAsync(ManifestEntry entry)
    {
        Directory.CreateDirectory(ModelFolder);
        var manifest = await LoadManifestAsync();
        manifest[_modelName] = entry;
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(ManifestPath, json);
    }

    /// <summary>Releases resources; this manager holds no unmanaged resources to release.</summary>
    public void Dispose()
    {
        // Nothing to release
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { /* best effort */ }
    }
}
