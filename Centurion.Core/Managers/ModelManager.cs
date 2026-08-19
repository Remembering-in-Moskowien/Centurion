using Centurion.Core.Abstractions;
using Centurion.Core.Exceptions;
using Centurion.Core.Metadata;
using Centurion.Core.Operators;
using Centurion.Core.Request;
using Centurion.Core.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace Centurion.Core.Managers;

/// <summary>
/// 模型管理器，负责模型文件的下载、校验和路径管理。
/// 支持单文件和目录模型。
/// </summary>
public class ModelManager : IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly string _modelName;
    private readonly ModelMeta _targetMeta;

    public string ModelFilePath { get; } // 单文件模式为文件路径，目录模式为目录路径
    public string ModelFolder { get; }
    public ModelMeta? TargetMeta => _targetMeta;
    public bool ManagementEnabled { get; }

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="modelName">模型名称</param>
    /// <param name="modelDict">模型元数据字典</param>
    /// <param name="serviceProvider">服务提供者</param>
    /// <param name="categoryFolder">模型分类文件夹名（如 whisper/diarization/vad）</param>
    public ModelManager(string modelName,
        IReadOnlyDictionary<string, ModelMeta> modelDict,
        IServiceProvider serviceProvider,
        string categoryFolder = "common")
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

        // 根据下载类型确定路径
        if (_targetMeta.DownloadType == ModelDownloadType.Directory)
        {
            // 目录模型：子目录为 models/categoryFolder/modelName/
            ModelFolder = Path.Combine(AppContext.BaseDirectory, "models", categoryFolder, _modelName);
            ModelFilePath = ModelFolder; // 将 ModelFilePath 设为目录路径
        }
        else
        {
            // 单文件模型：models/categoryFolder/fileName
            ModelFolder = Path.Combine(AppContext.BaseDirectory, "models", categoryFolder);
            ModelFilePath = Path.Combine(ModelFolder, _targetMeta.FileName!);
        }
    }

    public async Task EnsureModelAvailableAsync()
    {
        if (!ManagementEnabled) return;

        if (_targetMeta.DownloadType == ModelDownloadType.Directory)
        {
            await EnsureDirectoryModelAsync();
        }
        else
        {
            if (!File.Exists(ModelFilePath)) await DownloadModelAsync();
            // 不再进行任何哈希校验
        }
    }

    private async Task EnsureDirectoryModelAsync()
    {
        var dir = ModelFolder;
        Directory.CreateDirectory(dir);

        // 检查是否所有文件都存在
        var allFilesExist = _targetMeta.Files?.All(f => File.Exists(Path.Combine(dir, f))) ?? false;
        if (!allFilesExist)
        {
            ConsoleServices.Output.WriteLine($"Model directory '{_modelName}' is incomplete. Downloading...");
            await DownloadDirectoryModelAsync();
        }
    }

    private async Task DownloadDirectoryModelAsync()
    {
        if (_targetMeta.Files == null || _targetMeta.Files.Count == 0)
            throw new InvalidOperationException("No files specified for directory model.");

        Directory.CreateDirectory(ModelFolder);

        using var aria = _serviceProvider.GetRequiredService<AriaOperator>();

        // 下载所有文件
        var tasks = _targetMeta.Files.Select(async fileName =>
        {
            var fileUrl = _targetMeta.DownloadUrl!.TrimEnd('/') + "/" + fileName;
            var savePath = Path.Combine(ModelFolder, fileName);
            var request = new OperatorsRequest<AriaDownloadRequest>
            {
                Payload = new AriaDownloadRequest
                {
                    Url = fileUrl,
                    FullSavePath = savePath,
                    // 不再传递哈希
                }
            };
            await aria.ProcessAsync(request);
        });

        await Task.WhenAll(tasks);
        ConsoleServices.Output.WriteLine($"Model '{_modelName}' downloaded successfully.");
    }

    private async Task DownloadModelAsync()
    {
        if (!ManagementEnabled) return;
        Directory.CreateDirectory(ModelFolder);
        ConsoleServices.Output.WriteLine($"Model '{_modelName}' not found.");
        if (!await ConsoleServices.Confirm.ConfirmAsync("Continue with installation?"))
            throw new OperationCanceledException("User cancelled model download.");

        using var aria = _serviceProvider.GetRequiredService<AriaOperator>();
        var request = new OperatorsRequest<AriaDownloadRequest>
        {
            Payload = new AriaDownloadRequest
            {
                Url = _targetMeta.DownloadUrl!,
                FullSavePath = ModelFilePath,
                // 不再传递哈希
                SplitThread = 4,
                ServerConnection = 4,
                MaxRetry = 5,
                ProgressRefreshMs = 100
            }
        };

        await aria.ProcessAsync(request);
        ConsoleServices.Output.WriteLine($"Model '{_modelName}' downloaded successfully.");
    }

    public void Dispose()
    {
        // 无需释放
    }
}