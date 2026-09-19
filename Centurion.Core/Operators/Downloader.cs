using Centurion.Models.Console;
using Centurion.Abstractions;
using Centurion.Core.Infrastructure;
using Centurion.Core.Operators.Request;
using Centurion.Core.Operators.Response;
using Centurion.Core.Utils;
using Downloader;

namespace Centurion.Core.Operators;

/// <summary>
/// 基于 Downloader 库的下载算子，替代 aria2 外部调用
/// </summary>
public class Downloader : IOperator<AriaDownloadRequest, DownloaderResponse>
{
    private bool _disposed;

    /// <summary>
    /// 健康检查；本算子不再依赖外部二进制，始终直接返回成功。
    /// </summary>
    public Task CheckHealthAsync()
    {
        // 不再依赖外部二进制，直接返回成功
        return Task.CompletedTask;
    }

    /// <summary>
    /// 按请求多线程下载文件，显示下载进度，并在提供哈希值时进行 SHA256 校验。
    /// </summary>
    /// <param name="request">包含下载 URL、保存路径以及线程数、重试等配置的请求。</param>
    /// <param name="cancellationToken">取消下载操作的取消令牌。</param>
    /// <returns>下载结果，包含是否成功与保存文件路径。</returns>
    public async Task<DownloaderResponse> ProcessAsync(
        OperatorsRequest<AriaDownloadRequest> request,
        CancellationToken cancellationToken = default)
    {
        var payload = request.Payload;
        await CheckHealthAsync();

        // 生产安全：仅允许 HTTPS 下载，防止配置被篡改为明文 HTTP 造成中间人攻击
        if (!Uri.TryCreate(payload.Url, UriKind.Absolute, out var uri) || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Refusing to download from non-HTTPS URL: {payload.Url}");

        // 确保目标目录存在
        var targetDir = Path.GetDirectoryName(payload.FullSavePath)!;
        if (!Directory.Exists(targetDir))
            Directory.CreateDirectory(targetDir);

        // 配置下载器（依据官方文档）
        var config = new DownloadConfiguration
        {
            ParallelDownload = true,
            ChunkCount = payload.SplitThread,
            MaxTryAgainOnFailure = payload.MaxRetry == 0 ? int.MaxValue : payload.MaxRetry,
            BlockTimeout = 100000,
            BufferBlockSize = 1024 * 1024
        };

        using var downloader = new DownloadService(config);

        // 进度状态（线程安全）
        var progress = new DownloadProgressState();

        // 订阅下载进度事件
        downloader.DownloadProgressChanged += (_, e) =>
        {
            progress.TotalBytes = e.TotalBytesToReceive;
            progress.ReceivedBytes = e.ReceivedBytesSize;
        };

        // 启动下载任务（传入取消令牌）
        var downloadTask = downloader.DownloadFileTaskAsync(payload.Url, payload.FullSavePath, cancellationToken);

        // 使用 IProgressReporter 显示进度
        ConsoleServices.Progress.StartProgress("Downloading...", ctx =>
        {
            // 添加进度任务，最大值为总字节数（可能为 0，但后续会更新）
            var task = ctx.AddTask(
                $"Downloading {Path.GetFileName(payload.FullSavePath)}",
                (long)progress.TotalBytes
            );

            // 循环更新进度直至下载完成
            while (!downloadTask.IsCompleted)
            {
                cancellationToken.ThrowIfCancellationRequested();
                task.SetValue(progress.ReceivedBytes);
                // 如果总大小变化，更新最大值
                if (progress.TotalBytes > 0)
                    task.SetMaxValue((long)progress.TotalBytes);
                Thread.Sleep(payload.ProgressRefreshMs);
            }

            // 最终刷新至 100%
            task.SetValue(progress.TotalBytes);
            task.SetDescription("Download complete.");
            ctx.Refresh(); // 确保 UI 刷新
        });

        // 等待下载任务完成，若有异常将在此抛出
        await downloadTask;

        // 检查文件是否存在
        if (!File.Exists(payload.FullSavePath))
            throw new FileNotFoundException("Download completed but file not found.", payload.FullSavePath);

        // 执行 SHA256 校验（如果提供了哈希值）
        if (!string.IsNullOrEmpty(payload.FileHash))
        {
            var result = HashVerifier.VerifyHash(payload.FullSavePath, payload.FileHash);
            if (!result.IsMatch)
            {
                // 删除损坏文件
                File.Delete(payload.FullSavePath);
                throw new InvalidOperationException(
                    $"Hash mismatch. Expected: {payload.FileHash}, Actual: {result.ActualHash}");
            }
        }

        return new DownloaderResponse
        {
            Success = true,
            FilePath = payload.FullSavePath
        };
    }

    /// <summary>
    /// 释放资源并抑制终结器。
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 终结器，在对象回收时调用 <see cref="Dispose(bool)"/>。
    /// </summary>
    ~Downloader()
    {
        Dispose(false);
    }

    /// <summary>
    /// 释放资源；本类无需要释放的非托管资源。
    /// </summary>
    /// <param name="disposing">为 <see langword="true"/> 时同时释放托管资源。</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        // 无需要释放的非托管资源
        _disposed = true;
    }

    /// <summary>
    /// 线程安全的进度状态
    /// </summary>
    private sealed class DownloadProgressState
    {
        private readonly Lock _lock = new();

        public long TotalBytes
        {
            get
            {
                lock (_lock)
                {
                    return field;
                }
            }
            set
            {
                lock (_lock)
                {
                    field = value;
                }
            }
        }

        public long ReceivedBytes
        {
            get
            {
                lock (_lock)
                {
                    return field;
                }
            }
            set
            {
                lock (_lock)
                {
                    field = value;
                }
            }
        }
    }
}
