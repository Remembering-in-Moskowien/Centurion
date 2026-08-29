using Centurion.Core.Abstractions;
using Centurion.Core.Exceptions;
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

    public Task CheckHealthAsync()
    {
        // 不再依赖外部二进制，直接返回成功
        return Task.CompletedTask;
    }

    public async Task<DownloaderResponse> ProcessAsync(
        OperatorsRequest<AriaDownloadRequest> request,
        CancellationToken cancellationToken = default)
    {
        var payload = request.Payload;
        await CheckHealthAsync();

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

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~Downloader()
    {
        Dispose(false);
    }

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