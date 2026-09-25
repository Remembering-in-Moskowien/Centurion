using Centurion.Models.Workflow;
using Centurion.Abstractions.Utils;

using Centurion.Abstractions;
using Centurion.Models.Metadata;
using Centurion.Core.Operators.Request;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpCompress.Archives;

namespace Centurion.Core.Managers;

/// <summary>
/// 管理外部工具（ASR引擎）的下载、解压和路径。
/// 支持按推理设备（CUDA/Vulkan/CPU 等）自动选择工具的对应变体下载。
/// </summary>
public class ToolManager : IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ToolManager> _logger;
    private readonly string _toolsRoot;
    private readonly ToolMeta _toolMeta;

    /// <summary>工具解压后所在的根目录。</summary>
    public string ToolDirectory { get; private set; }
    /// <summary>工具主可执行文件的完整路径。</summary>
    public string ExecutablePath { get; private set; }

    /// <summary>工具运行时模型/权重的下载基础地址（可为空，为空时使用工具内置默认）。</summary>
    public string? ModelBaseUrl { get; }

    /// <summary>实际选用的设备变体（null 表示基础构建）。</summary>
    public string? ActiveVariantDescription { get; }

    /// <summary>
    /// 根据工具名称与目标推理设备，从注册表解析对应下载变体并初始化路径。
    /// </summary>
    /// <param name="toolName">要管理的工具名称。</param>
    /// <param name="registry">包含全部可用工具元数据的注册表。</param>
    /// <param name="device">目标推理设备，用于选择 CUDA/Vulkan/DirectML/CPU 等变体。</param>
    /// <param name="serviceProvider">服务提供者，用于解析日志等依赖。</param>
    public ToolManager(string toolName, ToolRegistry registry, InferenceDevice device, IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = serviceProvider.GetRequiredService<ILogger<ToolManager>>();
        _toolsRoot = Path.Combine(AppContext.BaseDirectory, "tools", "asr");
        Directory.CreateDirectory(_toolsRoot);

        if (!registry.Tools.TryGetValue(toolName, out var baseMeta))
            throw new ArgumentException($"Unsupported tool: {toolName}", nameof(toolName));

        // 按设备选择变体（精确匹配 → default → 基础字段）
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
    /// 解析工具在指定设备下应使用的下载信息（internal，便于单元测试）。
    /// 匹配顺序：设备键（cuda/vulkan/directml/cpu）→ "default" → 基础字段。
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
    /// 确保工具已下载并解压，若不存在则自动下载
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

        // 解压后可能因为嵌套目录导致 ExecutablePath 不存在，进行扁平化处理
        await NormalizeToolDirectoryAsync(cancellationToken);
    }

    private async Task DownloadAndExtractAsync(CancellationToken cancellationToken)
    {
        // 下载临时文件统一放入程序根目录下的临时目录，随句柄自动清理
        await using var tempDir = await _serviceProvider.GetRequiredService<ITempDirectoryManager>().CreateTempDirectoryAsync("tool_");
        var tempFile = Path.Combine(tempDir.Path, "download_archive");
        try
        {
            // 1. 下载
            using var downloader = _serviceProvider.GetRequiredService<Operators.Downloader>();
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

            // 2. 解压
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

                    // 防 zip-slip：拒绝任何会逃逸出工具目录的条目路径
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
    /// 扁平化工具目录：如果 ExecutablePath 不存在，则在子目录中查找可执行文件，
    /// 并将其所在目录的所有内容移至根目录，删除空目录。
    /// </summary>
    private async Task NormalizeToolDirectoryAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(ExecutablePath))
            return;

        var exeName = Path.GetFileName(ExecutablePath);
        _logger.LogWarning("Executable '{ExeName}' not found at expected path. Searching in subdirectories...", exeName);

        // 递归查找与可执行文件同名的文件
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
            // 已在根目录，但路径可能大小写不同，更新路径
            ExecutablePath = firstMatch;
            _logger.LogInformation("Executable found at {Path}", ExecutablePath);
            return;
        }

        // 将 sourceDir 下的所有内容移动到 ToolDirectory 根目录
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

        // 删除原空目录（及其可能的空父目录，但只删到根目录）
        DeleteEmptySubdirectories(ToolDirectory);

        // 更新 ExecutablePath
        var newExePath = Path.Combine(ToolDirectory, exeName);
        if (File.Exists(newExePath))
            ExecutablePath = newExePath;
        else
        {
            // 再次查找（可能被移动到其他位置，但理论上已在根目录）
            var newFound = Directory.GetFiles(ToolDirectory, exeName, SearchOption.TopDirectoryOnly);
            if (newFound.Length > 0)
                ExecutablePath = newFound[0];
        }

        _logger.LogInformation("Normalized executable path to {ExecutablePath}", ExecutablePath);
        await Task.CompletedTask; // 保持异步签名一致
    }

    /// <summary>
    /// 递归删除所有空子目录（不删除根目录）。
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
    /// 释放资源；本管理器无需释放非托管资源。
    /// </summary>
    public void Dispose() { }
}