using System.Diagnostics;
using System.Text.RegularExpressions;
using Centurion.Core.Exceptions;
using Centurion.Core.Operators.Payload;
using Centurion.Core.Tools;
// localization removed; strings hard-coded

namespace Centurion.Core.Operators;

public class AriaOperator : IOperators
{
    // localization removed
    private readonly IBinaryLocator _binaryLocator;
    private string? _aria2BinaryPath;
    private Process? _runningAriaProcess;
    private bool _disposed;

    private static readonly Regex ProgressLineRegex = new(
        @"\[.+?([0-9.]+[KMG]iB)/([0-9.]+[KMG]iB)\(([0-9]{1,3})%\)\s*CN:[0-9.]+\s*DL:([0-9.]+[KMG]iB).*ETA:([^\s]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// 构造函数。
    /// </summary>
    public AriaOperator(IBinaryLocator binaryLocator)
    {
        _binaryLocator = binaryLocator;
    }

    public async Task EnsureTargetAvailableAsync()
    {
        if (!string.IsNullOrEmpty(_aria2BinaryPath) && File.Exists(_aria2BinaryPath))
            return;

        var bin = OperatingSystem.IsWindows() ? "aria2c.exe" : "aria2c";
        _aria2BinaryPath = _binaryLocator.Locate(bin, "aria");
    }

    public async Task<TResult> SendRequestAsync<TResult, TPayload>(
        OperatorsRequest<TPayload> request,
        CancellationToken cancellationToken = default)
    {
        await EnsureTargetAvailableAsync();
        if (request.Payload is not AriaDownloadPayload payload)
            throw new ArgumentException("Payload type must be AriaDownloadPayload", nameof(request));

        // 前置校验：目录创建、磁盘空间预检查可在此扩展
        var targetDir = Path.GetDirectoryName(payload.FullSavePath)!;
        if (!Directory.Exists(targetDir))
            Directory.CreateDirectory(targetDir);

        // 构造进程启动参数，使用ArgumentList杜绝命令注入
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
        args.Add("--console-log-level=warn");
        args.Add("--summary-interval=1");
        args.Add("--enable-color=false");
        args.Add("-d");
        args.Add(targetDir);
        args.Add("-o");
        args.Add(Path.GetFileName(payload.FullSavePath));
        args.Add(payload.Url);

        // 启动进程并持有引用，用于Dispose/取消时Kill
        using var process = Process.Start(startInfo)
                    ?? throw new InvalidOperationException("Failed to start aria2 process.");
        _runningAriaProcess = process;

        var progressState = new AriaProgressState();
        // 绑定输出事件
        process.OutputDataReceived += (_, e) => ParseLogLine(e.Data, progressState);
        process.ErrorDataReceived += (_, e) => ParseLogLine(e.Data, progressState);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // 等待第一条进度输出初始化状态
        await WaitForFirstProgressAsync(progressState, payload.ProgressRefreshMs, cancellationToken);

        // 渲染进度条
        ConsoleServices.Progress.StartProgress("Downloading...", ctx =>
        {
            var task = ctx.AddTask(
                string.Format("Downloading {0}", Path.GetFileName(payload.FullSavePath)),
                (long)progressState.TotalBytes
            );

            while (!process.HasExited)
            {
                cancellationToken.ThrowIfCancellationRequested();
                task.SetValue((long)progressState.DownloadedBytes);
                Thread.Sleep(payload.ProgressRefreshMs);
            }

            task.SetValue((long)progressState.TotalBytes);
            task.SetDescription("Download complete, verifying file hash...");
        });

        // 资源清理
        _runningAriaProcess = null;

        // 取消校验
        cancellationToken.ThrowIfCancellationRequested();

        // 进程退出码校验
        if (process.ExitCode != 0)
            throw new AriaProcessExitException(
                string.Format("aria2 failed with exit code {0} while downloading Whisper model.", process.ExitCode), process.ExitCode);

        // 文件存在校验
        if (!File.Exists(payload.FullSavePath))
            throw new FileNotFoundException("File Download completed, but the file was not found.", payload.FullSavePath);

        // 哈希校验
        var hashResult = SubTools.VerifyHash(payload.FullSavePath, payload.FileHash);
        if (!hashResult.IsMatch)
            throw new FileHashMismatchException(
                string.Format("File hash mismacthed. Hope: {0};Real: {1}", payload.FileHash, hashResult.ActualHash),
                payload.FullSavePath,
                payload.FileHash,
                hashResult.ActualHash);

        // 泛型返回，如需自定义结果可新建DownloadResult实体
        return (TResult)(object)new { Success = true, FilePath = payload.FullSavePath };
    }

    private static string GetAriaBinaryFileName()
    {
        return OperatingSystem.IsWindows() ? "aria2c.exe" : "aria2c";
    }

    private static void ParseLogLine(string? line, AriaProgressState state)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        var match = ProgressLineRegex.Match(line);
        if (!match.Success) return;

        state.DownloadedBytes = SubTools.ParseSizeToBytes(match.Groups[1].Value);
        state.TotalBytes = SubTools.ParseSizeToBytes(match.Groups[2].Value);
    }

    private async Task WaitForFirstProgressAsync(
        AriaProgressState state, int delayMs, CancellationToken ct)
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
            // 托管资源：终止运行中的aria2进程
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

    /// <summary>线程安全进度容器，读写加锁避免并发脏读</summary>
    private sealed class AriaProgressState
    {
        private readonly Lock _lock = new();
        private double _downloaded;
        private double _total;

        public double DownloadedBytes
        {
            get
            {
                lock (_lock)
                {
                    return _downloaded;
                }
            }
            set
            {
                lock (_lock)
                {
                    _downloaded = value;
                }
            }
        }

        public double TotalBytes
        {
            get
            {
                lock (_lock)
                {
                    return _total;
                }
            }
            set
            {
                lock (_lock)
                {
                    _total = value;
                }
            }
        }
    }
}