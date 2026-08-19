using System.Diagnostics;
using System.Text.RegularExpressions;
using Centurion.Core.Abstractions;
using Centurion.Core.Exceptions;
using Centurion.Core.Request;
using Centurion.Core.Response;
using Centurion.Core.Tools;

namespace Centurion.Core.Operators;

/// <summary>
/// Aria2 下载算子，用于下载模型文件等
/// </summary>
public class AriaOperator(IBinaryLocator binaryLocator) : IOperator<AriaDownloadRequest, AriaDownloadResponse>
{
    private string? _aria2BinaryPath;
    private Process? _runningAriaProcess;
    private bool _disposed;

    private static readonly Regex ProgressLineRegex = new(
        @"\[.+?([0-9.]+[KMG]iB)/([0-9.]+[KMG]iB)\(([0-9]{1,3})%\)\s*CN:[0-9.]+\s*DL:([0-9.]+[KMG]iB).*ETA:([^\s]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public async Task EnsureTargetAvailableAsync()
    {
        if (!string.IsNullOrEmpty(_aria2BinaryPath) && File.Exists(_aria2BinaryPath))
            return;

        var bin = OperatingSystem.IsWindows() ? "aria2c.exe" : "aria2c";
        _aria2BinaryPath = binaryLocator.Locate(bin, "aria");
    }

    public async Task<AriaDownloadResponse> ProcessAsync(
        OperatorsRequest<AriaDownloadRequest> request,
        CancellationToken cancellationToken = default)
    {
        var payload = request.Payload;
        await EnsureTargetAvailableAsync();

        // 前置校验
        var targetDir = Path.GetDirectoryName(payload.FullSavePath)!;
        if (!Directory.Exists(targetDir))
            Directory.CreateDirectory(targetDir);

        // 构造进程启动参数
        var startInfo = new ProcessStartInfo(_aria2BinaryPath!)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        var args = startInfo.ArgumentList;
        args.Add("--continue=true");
        args.Add("-x");
        args.Add(payload.SplitThread.ToString());
        args.Add("-s");
        args.Add(payload.ServerConnection.ToString());
        args.Add("--max-tries");
        args.Add(payload.MaxRetry.ToString());
        args.Add("--console-log-level=info");
        args.Add("--summary-interval=1");
        args.Add("--enable-color=false");
        args.Add("-d");
        args.Add(targetDir);
        args.Add("-o");
        args.Add(Path.GetFileName(payload.FullSavePath));
        args.Add(payload.Url);

        // 不再添加 --checksum

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("Failed to start aria2 process.");
        _runningAriaProcess = process;

        var progressState = new AriaProgressState();
        process.OutputDataReceived += (_, e) => ParseLogLine(e.Data, progressState);
        process.ErrorDataReceived += (_, e) => ParseLogLine(e.Data, progressState);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // 等待第一条进度输出
        await WaitForFirstProgressAsync(progressState, payload.ProgressRefreshMs, cancellationToken);

        // 渲染进度条
        ConsoleServices.Progress.StartProgress("Downloading...", ctx =>
        {
            var task = ctx.AddTask(
                $"Downloading {Path.GetFileName(payload.FullSavePath)}",
                (long)progressState.TotalBytes
            );

            while (!process.HasExited)
            {
                cancellationToken.ThrowIfCancellationRequested();
                task.SetValue((long)progressState.DownloadedBytes);
                Thread.Sleep(payload.ProgressRefreshMs);
            }

            task.SetValue((long)progressState.TotalBytes);
            task.SetDescription("Download complete.");
        });

        _runningAriaProcess = null;

        cancellationToken.ThrowIfCancellationRequested();

        if (process.ExitCode != 0)
            throw new AriaProcessExitException(
                $"aria2 failed with exit code {process.ExitCode} while downloading.",
                process.ExitCode);

        if (!File.Exists(payload.FullSavePath))
            throw new FileNotFoundException("Download completed but file not found.", payload.FullSavePath);

        // 不再进行任何哈希校验

        return new AriaDownloadResponse
        {
            Success = true,
            FilePath = payload.FullSavePath
        };
    }

    private static void ParseLogLine(string? line, AriaProgressState state)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        var match = ProgressLineRegex.Match(line);
        if (!match.Success) return;

        state.DownloadedBytes = SubTools.ParseSizeToBytes(match.Groups[1].Value);
        state.TotalBytes = SubTools.ParseSizeToBytes(match.Groups[2].Value);
    }

    private async Task WaitForFirstProgressAsync(AriaProgressState state, int delayMs, CancellationToken ct)
    {
        while (state.TotalBytes == 0)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(delayMs, ct);
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~AriaOperator()
    {
        Dispose(false);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            if (_runningAriaProcess is { HasExited: false } process)
            {
                process.Kill();
                process.WaitForExit();
            }
            _runningAriaProcess?.Dispose();
            _runningAriaProcess = null;
        }

        _disposed = true;
    }

    private sealed class AriaProgressState
    {
        private readonly Lock _lock = new();
        private double _downloaded;
        private double _total;

        public double DownloadedBytes
        {
            get { lock (_lock) return _downloaded; }
            set { lock (_lock) _downloaded = value; }
        }

        public double TotalBytes
        {
            get { lock (_lock) return _total; }
            set { lock (_lock) _total = value; }
        }
    }
}