using System.Diagnostics;
using System.Text;
using Centurion.Core.Operators.Payload;
using Centurion.Core.Exceptions;
using Centurion.Core.Tools;
using Centurion.Core.Operators.Results;

namespace Centurion.Core.Operators;

/// <summary>FFmpeg音频转换算子，实现统一IOperators接口</summary>
public class FFmpegOperator : IOperators
{
    private string? _ffmpegBinaryPath;
    private Process? _runningProcess;
    private bool _disposed;

    public async Task EnsureTargetAvailableAsync()
    {
        if (!string.IsNullOrEmpty(_ffmpegBinaryPath) && File.Exists(_ffmpegBinaryPath))
            return;

        var bin = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        _ffmpegBinaryPath = BinaryLocator.Locate(bin, "ffmpeg");
    }

    public async Task<TResult> SendRequestAsync<TResult, TPayload>(
        OperatorsRequest<TPayload> request,
        CancellationToken cancellationToken = default)
    {
        await EnsureTargetAvailableAsync();

        // 校验载荷类型
        if (request.Payload is not FFmpegConvertPayload payload)
            throw new ArgumentException(Localization.Get("FFmpegPayloadError"), nameof(request));

        // 前置文件校验
        if (!File.Exists(payload.InputFilePath))
            throw new FileNotFoundException(Localization.Get("FileNotFoundInput"), payload.InputFilePath);

        var startInfo = new ProcessStartInfo(_ffmpegBinaryPath!)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            StandardErrorEncoding = Encoding.UTF8
        };
        var args = startInfo.ArgumentList;

        // 统一基础音频转Whisper参数
        //ffmpeg -y -i test.mp4 -ac 1 -ar 16000 -acodec pcm_s16le -af aresample -f wav -
        args.Add("-y");
        args.Add("-i");
        args.Add(payload.InputFilePath);
        args.Add("-ac");
        args.Add("1");
        args.Add("-ar");
        args.Add("16000");
        args.Add("-acodec");
        args.Add("pcm_s16le");
        args.Add("-af");
        args.Add("aresample");
        args.Add("-f");
        args.Add("wav");

        var outputFile = string.Empty;

        // 输出到本地文件
        outputFile = payload.OutputFilePath!;
        var targetDir = Path.GetDirectoryName(outputFile)!;
        if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);
        args.Add(outputFile);

        // 启动进程并持有用于Dispose/取消销毁
        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException(Localization.Get("FailedStartFFmpegProcess"));
        _runningProcess = process;

        StringBuilder errorBuilder = new();
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                errorBuilder.AppendLine(e.Data);
        };
        process.BeginErrorReadLine();

        cancellationToken.Register(() =>
        {
            if (!process.HasExited) process.Kill();
        });

        // 等待进程退出（支持取消）
        await process.WaitForExitAsync(cancellationToken);
        _runningProcess = null;
        cancellationToken.ThrowIfCancellationRequested();

        var errorLog = errorBuilder.ToString();
        if (process.ExitCode != 0)
        {
            throw new FFmpegProcessExitException(
                Localization.Get("FFmpegConversionFailed", process.ExitCode),
                process.ExitCode, errorLog);
        }

        if (!File.Exists(outputFile)) throw new FileNotFoundException(Localization.Get("FFmpegOutputFileMissing"), outputFile);

        ConsoleServices.Output.WriteLine(Localization.Get("FFmpegSuccessPath", outputFile));
        var fileResult = new FFmpegFileResult { OutputPath = outputFile };
        return (TResult)(object)fileResult;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~FFmpegOperator()
    {
        Dispose(false);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing)
        {
            // 杀死后台运行的ffmpeg进程，防止残留
            if (_runningProcess != null && !_runningProcess.HasExited)
            {
                _runningProcess.Kill();
                _runningProcess.WaitForExit();
            }

            _runningProcess?.Dispose();
            _runningProcess = null;
        }

        _disposed = true;
    }
}