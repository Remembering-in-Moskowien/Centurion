using Centurion.Abstractions;
using Centurion.Core.Capabilities.Infrastructure;using Centurion.Models.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Centurion.Core.Operators.Download;
using Centurion.Core.Operators.Download.Request;
using Centurion.Models.Console;
namespace Centurion.Core.Capabilities.Managers.Media;

/// <summary>
/// Model manager responsible for downloading, verifying, and managing paths for model files.
/// Supports both single-file and directory models.
/// </summary>
public class ModelManager : IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly string _modelName;
    private readonly ModelMeta _targetMeta;

    /// <summary>Path to the model file; a file path in single-file mode, a directory path in directory mode.</summary>
    public string ModelFilePath { get; } // File path in single-file mode, directory path in directory mode
    /// <summary>Path to the folder containing the model.</summary>
    public string ModelFolder { get; }
    /// <summary>Metadata for the current model; <see langword="null"/> when management is disabled.</summary>
    public ModelMeta? TargetMeta => _targetMeta;
    /// <summary>Whether model management is enabled (<see langword="false"/> when the model name is empty).</summary>
    public bool ManagementEnabled { get; }

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="modelName">Model name</param>
    /// <param name="modelDict">Model metadata dictionary</param>
    /// <param name="serviceProvider">Service provider</param>
    /// <param name="categoryFolder">Category folder name for the model (e.g. whisper/diarization/vad)</param>
    /// <param name="modelsRoot">Optional models root directory (defaults to AppContext.BaseDirectory); the model lands under modelsRoot/models/{categoryFolder}/{modelName}.</param>
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
        ModelFilePath = string.Empty;

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
        // Determine the path based on the download type
        if (_targetMeta.DownloadType is ModelDownloadType.Directory or ModelDownloadType.OnnxModelDirectory)
        {
            // Directory model: subdirectory is models/categoryFolder/modelName/
            ModelFolder = Path.Combine(root, "models", categoryFolder, _modelName);
            ModelFilePath = ModelFolder; // Set ModelFilePath to the directory path
        }
        else
        {
            // Single-file model: models/categoryFolder/fileName
            ModelFolder = Path.Combine(root, "models", categoryFolder);
            ModelFilePath = Path.Combine(ModelFolder, _targetMeta.FileName!);
        }
    }

    /// <summary>
    /// Checks model integrity; throws <see cref="ModelMissingException"/> when files are missing
    /// (prompting the user to install via <c>Centurion models install &lt;model&gt;</c>), without
    /// any automatic download. Returns directly when management is disabled.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    public async Task CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        if (!ManagementEnabled) return;

        var missing = FindMissingEntries();
        if (missing.Count > 0)
            throw new ModelMissingException(_modelName, ModelFilePath, missing);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Installs the model: downloads the required files when missing/incomplete (used by the
    /// models install command).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    public async Task EnsureInstalledAsync(CancellationToken cancellationToken = default)
    {
        if (!ManagementEnabled) return;

        if (_targetMeta.DownloadType is ModelDownloadType.Directory or ModelDownloadType.OnnxModelDirectory)
        {
            await EnsureDirectoryModelAsync(cancellationToken);
        }
        else
        {
            if (!File.Exists(ModelFilePath)) await DownloadModelAsync(cancellationToken);
            // No longer perform any hash verification
        }
    }

    /// <summary>Returns the missing model file entries (empty list = ready).</summary>
    private IReadOnlyList<string> FindMissingEntries()
    {
        if (_targetMeta.DownloadType is ModelDownloadType.Directory or ModelDownloadType.OnnxModelDirectory)
        {
            var files = _targetMeta.Files ?? [];
            return files.Where(f => !File.Exists(Path.Combine(ModelFolder, f))).ToList();
        }
        return File.Exists(ModelFilePath) ? [] : [Path.GetFileName(ModelFilePath)];
    }

    private async Task EnsureDirectoryModelAsync(CancellationToken cancellationToken = default)
    {
        var dir = ModelFolder;
        Directory.CreateDirectory(dir);

        // Check whether all files exist
        var allFilesExist = _targetMeta.Files?.All(f => File.Exists(Path.Combine(dir, f))) ?? false;
        if (!allFilesExist)
        {
            ConsoleServices.Output.WriteInfo($"Model directory '{_modelName}' is incomplete. Downloading...");
            await DownloadDirectoryModelAsync(cancellationToken);
        }
    }

    private async Task DownloadDirectoryModelAsync(CancellationToken cancellationToken = default)
    {
        if (_targetMeta.Files == null || _targetMeta.Files.Count == 0)
            throw new InvalidOperationException("No files specified for directory model.");

        Directory.CreateDirectory(ModelFolder);

        using var aria = _serviceProvider.GetRequiredService<Centurion.Core.Operators.Download.Downloader>();

        // Download all files
        var tasks = _targetMeta.Files.Select(async fileName =>
        {
            var fileUrl = _targetMeta.FileUrls != null && _targetMeta.FileUrls.TryGetValue(fileName, out var overrideUrl)
                ? overrideUrl
                : _targetMeta.DownloadUrl!.TrimEnd('/') + "/" + fileName;
            var savePath = Path.Combine(ModelFolder, fileName);
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

        await Task.WhenAll(tasks);
        ConsoleServices.Output.WriteInfo($"Model '{_modelName}' downloaded successfully.");
    }

    private async Task DownloadModelAsync(CancellationToken cancellationToken = default)
    {
        if (!ManagementEnabled) return;
        Directory.CreateDirectory(ModelFolder);
        ConsoleServices.Output.WriteInfo($"Model '{_modelName}' not found.");
        cancellationToken.ThrowIfCancellationRequested();

        using var aria = _serviceProvider.GetRequiredService<Centurion.Core.Operators.Download.Downloader>();
        var request = new OperatorsRequest<AriaDownloadRequest>
        {
            Payload = new AriaDownloadRequest
            {
                Url = _targetMeta.DownloadUrl!,
                FullSavePath = ModelFilePath,
                FileHash = _targetMeta.FileHash ?? string.Empty,
                SplitThread = 4,
                ServerConnection = 4,
                MaxRetry = 5,
                ProgressRefreshMs = 100
            }
        };

        await aria.ProcessAsync(request, cancellationToken);
        ConsoleServices.Output.WriteLine($"Model '{_modelName}' downloaded successfully.");
    }

    /// <summary>
    /// Releases resources; this manager holds no unmanaged resources to release.
    /// </summary>
    public void Dispose()
    {
        // Nothing to release
    }
}