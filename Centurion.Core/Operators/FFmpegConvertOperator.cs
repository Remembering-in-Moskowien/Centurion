using System.Diagnostics;
using System.Text;
using Centurion.Core.Abstractions;
using Centurion.Core.Exceptions;
using Centurion.Core.Request;
using Centurion.Core.Response;
using Centurion.Core.Tools;

namespace Centurion.Core.Operators;

/// <summary>
/// FFmpeg 音频转换算子（转 16kHz 单声道 WAV）
/// </summary>
public class FFmpegConvertOperator(IBinaryLocator binaryLocator)
    : IOperator<FFmpegConvertRequest, FFmpegConvertResponse>, IDisposable
{
    private string? _ffmpegBinaryPath;
    private Process? _runningProcess;
    private bool _disposed;

    public async Task EnsureTargetAvailableAsync()
    {
        if (!string.IsNullOrEmpty(_ffmpegBinaryPath) && File.Exists(_ffmpegBinaryPath))
            return;

        var bin = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        _ffmpegBinaryPath = binaryLocator.Locate(bin, "ffmpeg");
        if (string.IsNullOrEmpty(_ffmpegBinaryPath) || !File.Exists(_ffmpegBinaryPath))
            throw new FileNotFoundException("FFmpeg executable not found.");
    }

    public async Task<FFmpegConvertResponse> ProcessAsync(
        OperatorsRequest<FFmpegConvertRequest> request,
        CancellationToken cancellationToken = default)
    {
        await EnsureTargetAvailableAsync();
        return await ConvertAudioAsync(request.Payload, cancellationToken);
    }

    private async Task<FFmpegConvertResponse> ConvertAudioAsync(FFmpegConvertRequest payload, CancellationToken ct)
    {
        if (!File.Exists(payload.InputFilePath))
            throw new FileNotFoundException("Input file not found", payload.InputFilePath);

        var outputFile = payload.OutputFilePath!;
        var targetDir = Path.GetDirectoryName(outputFile)!;
        if (!Directory.Exists(targetDir))
            Directory.CreateDirectory(targetDir);

        var args = $"-y -i \"{payload.InputFilePath}\" -ac 1 -ar 16000 -acodec pcm_s16le -af aresample -f wav \"{outputFile}\"";

        using var process = StartProcess(args);
        var errorBuilder = new StringBuilder();
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                errorBuilder.AppendLine(e.Data);
        };
        process.BeginErrorReadLine();

        ct.Register(() =>
        {
            if (!process.HasExited) process.Kill();
        });

        await process.WaitForExitAsync(ct);
        ct.ThrowIfCancellationRequested();

        if (process.ExitCode != 0)
            throw new FFmpegProcessExitException(
                $"FFmpeg conversion failed ExitCode:{process.ExitCode}\nLog:{errorBuilder}",
                process.ExitCode, errorBuilder.ToString());

        if (!File.Exists(outputFile))
            throw new FileNotFoundException($"FFmpeg did not produce output file: {outputFile}", outputFile);

        ConsoleServices.Output?.WriteLine($"FFmpeg succeeded: {outputFile}");
        return new FFmpegConvertResponse { OutputPath = outputFile };
    }

    private Process StartProcess(string arguments)
    {
        var psi = new ProcessStartInfo(_ffmpegBinaryPath!, arguments)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            StandardErrorEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8
        };
        var process = Process.Start(psi);
        if (process == null)
            throw new InvalidOperationException("Failed to start FFmpeg process.");
        return process;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~FFmpegConvertOperator()
    {
        Dispose(false);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing)
        {
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