using Centurion.Abstractions;
using Centurion.Core.Capabilities.Infrastructure;using Centurion.Models.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Centurion.Core.Operators.Download;
using Centurion.Core.Operators.Download.Request;
using Centurion.Models.Console;
namespace Centurion.Core.Capabilities.Managers.Media;

/// <summary>
/// 模型管理器，负责模型文件的下载、校验和路径管理。
/// 支持单文件和目录模型。
/// </summary>
public class ModelManager : IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly string _modelName;
    private readonly ModelMeta _targetMeta;

    /// <summary>模型文件路径；单文件模式为文件路径，目录模式为目录路径。</summary>
    public string ModelFilePath { get; } // 单文件模式为文件路径，目录模式为目录路径
    /// <summary>模型所在文件夹路径。</summary>
    public string ModelFolder { get; }
    /// <summary>当前模型对应的元数据；未启用管理时为 <see langword="null"/>。</summary>
    public ModelMeta? TargetMeta => _targetMeta;
    /// <summary>是否启用模型管理（模型名称为空时为 <see langword="false"/>）。</summary>
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
        if (_targetMeta.DownloadType is ModelDownloadType.Directory or ModelDownloadType.OnnxModelDirectory)
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

    /// <summary>
    /// 检查模型完整性；缺失或不完整时自动下载所需模型文件，未启用管理时直接返回。
    /// </summary>
    /// <param name="cancellationToken">取消操作的取消令牌。</param>
    public async Task CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        if (!ManagementEnabled) return;

        if (_targetMeta.DownloadType is ModelDownloadType.Directory or ModelDownloadType.OnnxModelDirectory)
        {
            await EnsureDirectoryModelAsync(cancellationToken);
        }
        else
        {
            if (!File.Exists(ModelFilePath)) await DownloadModelAsync(cancellationToken);
            // 不再进行任何哈希校验
        }
    }

    private async Task EnsureDirectoryModelAsync(CancellationToken cancellationToken = default)
    {
        var dir = ModelFolder;
        Directory.CreateDirectory(dir);

        // 检查是否所有文件都存在
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
    /// 释放资源；本管理器无需释放任何非托管资源。
    /// </summary>
    public void Dispose()
    {
        // 无需释放
    }
}