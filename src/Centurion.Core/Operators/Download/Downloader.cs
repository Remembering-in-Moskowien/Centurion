using Centurion.Abstractions;
using Centurion.Core.Capabilities.Infrastructure;using Downloader;
using Centurion.Core.Operators.Download.Request;
using Centurion.Core.Operators.Download.Response;
using Centurion.Core.Utils.Infrastructure;
using Centurion.Models.Console;

namespace Centurion.Core.Operators.Download;

/// <summary>
/// Download operator based on the Downloader library, replacing the external aria2 invocation.
/// </summary>
public class Downloader : IOperator<AriaDownloadRequest, DownloaderResponse>
{
    private bool _disposed;

    /// <summary>
    /// Health check; this operator no longer relies on an external binary and always returns success directly.
    /// </summary>
    public Task CheckHealthAsync()
    {
        // No longer relies on an external binary; return success directly.
        return Task.CompletedTask;
    }

    /// <summary>
    /// Downloads the file in multiple threads as requested, shows download progress, and performs SHA256 verification when a hash is provided.
    /// </summary>
    /// <param name="request">Request containing the download URL, save path, and configuration such as thread count and retries.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the download.</param>
    /// <returns>The download result, including whether it succeeded and the saved file path.</returns>
    public async Task<DownloaderResponse> ProcessAsync(
        OperatorsRequest<AriaDownloadRequest> request,
        CancellationToken cancellationToken = default)
    {
        var payload = request.Payload;
        await CheckHealthAsync();

        // Production safety: only allow HTTPS downloads, preventing a tampered config from falling back to plaintext HTTP and enabling a man-in-the-middle attack.
        if (!Uri.TryCreate(payload.Url, UriKind.Absolute, out var uri) || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Refusing to download from non-HTTPS URL: {payload.Url}");

        // GitHub release URLs are frequently unreachable from CN networks (TLS resets), so the
        // GitHubDownloadProxy candidate chain (user mirror -> default mirrors -> direct, configurable
        // via --github-proxy / --no-github-proxy) is tried in order; hf-mirror model URLs pass through
        // unchanged. This makes first-run tool downloads work out of the box.
        var candidates = GitHubDownloadProxy.CandidateUrls(payload.Url).ToList();

        Exception? lastError = null;
        foreach (var candidate in candidates)
        {
            try
            {
                await DownloadCoreAsync(candidate, payload, cancellationToken);
                return new DownloaderResponse { Success = true, FilePath = payload.FullSavePath };
            }
            catch (Exception ex) when (IsNetworkFailure(ex) && candidate != candidates[^1])
            {
                lastError = ex;
            }
        }
        throw lastError ?? new InvalidOperationException($"Download failed for {payload.Url}.");
    }

    /// <summary>Downloads a single URL to the target path with progress reporting and optional SHA256 verification.</summary>
    private static async Task DownloadCoreAsync(string url, AriaDownloadRequest payload, CancellationToken cancellationToken)
    {
        // Ensure the target directory exists.
        var targetDir = Path.GetDirectoryName(payload.FullSavePath)!;
        if (!Directory.Exists(targetDir))
            Directory.CreateDirectory(targetDir);

        // Configure the downloader (per the official documentation).
        var config = new DownloadConfiguration
        {
            ParallelDownload = true,
            ChunkCount = payload.SplitThread,
            MaxTryAgainOnFailure = payload.MaxRetry == 0 ? int.MaxValue : payload.MaxRetry,
            BlockTimeout = 100000,
            BufferBlockSize = 1024 * 1024
        };

        using var downloader = new DownloadService(config);

        // Progress state (thread-safe).
        var progress = new DownloadProgressState();

        // Subscribe to download progress events.
        downloader.DownloadProgressChanged += (_, e) =>
        {
            progress.TotalBytes = e.TotalBytesToReceive;
            progress.ReceivedBytes = e.ReceivedBytesSize;
        };

        // Start the download task (pass the cancellation token).
        var downloadTask = downloader.DownloadFileTaskAsync(url, payload.FullSavePath, cancellationToken);

        // Use IProgressReporter to display progress.
        ConsoleServices.Progress.StartProgress("Downloading...", ctx =>
        {
            // Add a progress task with max value = total bytes (may be 0, but updated later).
            var task = ctx.AddTask(
                $"Downloading {Path.GetFileName(payload.FullSavePath)}",
                (long)progress.TotalBytes
            );

            // Loop updating progress until the download completes.
            while (!downloadTask.IsCompleted)
            {
                cancellationToken.ThrowIfCancellationRequested();
                task.SetValue(progress.ReceivedBytes);
                // If the total size changes, update the max value.
                if (progress.TotalBytes > 0)
                    task.SetMaxValue((long)progress.TotalBytes);
                Thread.Sleep(payload.ProgressRefreshMs);
            }

            // Finally refresh to 100%.
            task.SetValue(progress.TotalBytes);
            task.SetDescription("Download complete.");
            ctx.Refresh(); // Ensure the UI refreshes.
        });

        // Wait for the download task to complete; any exception is rethrown here.
        await downloadTask;

        // Check whether the file exists.
        if (!File.Exists(payload.FullSavePath))
            throw new FileNotFoundException("Download completed but file not found.", payload.FullSavePath);

        // Perform SHA256 verification (if a hash was provided).
        if (!string.IsNullOrEmpty(payload.FileHash))
        {
            var result = HashVerifier.VerifyHash(payload.FullSavePath, payload.FileHash);
            if (!result.IsMatch)
            {
                // Delete the corrupted file.
                File.Delete(payload.FullSavePath);
                throw new InvalidOperationException(
                    $"Hash mismatch. Expected: {payload.FileHash}, Actual: {result.ActualHash}");
            }
        }
    }

    /// <summary>Network-class failures eligible for switching to the next mirror candidate.</summary>
    private static bool IsNetworkFailure(Exception ex) =>
        ex is HttpRequestException or IOException or OperationCanceledException;

    /// <summary>
    /// Releases resources and suppresses the finalizer.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Finalizer; calls <see cref="Dispose(bool)"/> when the object is garbage-collected.
    /// </summary>
    ~Downloader()
    {
        Dispose(false);
    }

    /// <summary>
    /// Releases resources; this class has no unmanaged resources to release.
    /// </summary>
    /// <param name="disposing">Also releases managed resources when <see langword="true"/>.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        // No unmanaged resources to release.
        _disposed = true;
    }

    /// <summary>
    /// Thread-safe progress state.
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
