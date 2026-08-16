using System.Diagnostics;
using System.Globalization;
using System.Text;
using Centurion.Core.Exceptions;
using Centurion.Core.Operators.Payload;
using Centurion.Core.Operators.Results;
using Centurion.Core.Tools;
using Microsoft.Extensions.Localization;

namespace Centurion.Core.Operators;

/// <summary>
/// FFmpeg 算子：支持音频转换和按时间段切割。
/// 统一通过 SendRequestAsync 入口，根据载荷类型自动分发。
/// </summary>
public class FFmpegOperator(IStringLocalizer<Localization> localizer, IBinaryLocator binaryLocator)
    : IOperators
{
    private readonly IStringLocalizer<Localization> _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
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
            throw new FileNotFoundException(_localizer["FFmpegNotFound"]);
    }

    public async Task<TResult> SendRequestAsync<TResult, TPayload>(
        OperatorsRequest<TPayload> request,
        CancellationToken cancellationToken = default)
    {
        await EnsureTargetAvailableAsync();

        if (request.Payload is FFmpegConvertPayload convertPayload)
        {
            var result = await ConvertAudioAsync(convertPayload, cancellationToken);
            if (typeof(TResult) != typeof(FFmpegConvertResult))
                throw new InvalidOperationException(_localizer["UnexpectedResultType", typeof(FFmpegConvertResult).Name, typeof(TResult).Name]);
            return (TResult)(object)result;
        }
        else if (request.Payload is FFmpegSplitPayload splitPayload)
        {
            var result = await SplitAudioAsync(splitPayload, cancellationToken);
            if (typeof(TResult) != typeof(FFmpegSplitResult))
                throw new InvalidOperationException(_localizer["UnexpectedResultType", typeof(FFmpegSplitResult).Name, typeof(TResult).Name]);
            return (TResult)(object)result;
        }
        else
        {
            throw new ArgumentException(_localizer["UnsupportedPayloadType"], nameof(request));
        }
    }

    /// <summary>音频转换（16kHz 单声道 WAV）</summary>
    private async Task<FFmpegConvertResult> ConvertAudioAsync(FFmpegConvertPayload payload, CancellationToken ct)
    {
        if (!File.Exists(payload.InputFilePath))
            throw new FileNotFoundException(_localizer["FileNotFoundInput"], payload.InputFilePath);

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

        ct.Register(() => { if (!process.HasExited) process.Kill(); });

        await process.WaitForExitAsync(ct);
        ct.ThrowIfCancellationRequested();

        if (process.ExitCode != 0)
        {
            throw new FFmpegProcessExitException(
                _localizer["FFmpegConversionFailed", process.ExitCode],
                process.ExitCode, errorBuilder.ToString());
        }

        if (!File.Exists(outputFile))
            throw new FileNotFoundException(_localizer["FFmpegOutputFileMissing"], outputFile);

        ConsoleServices.Output?.WriteLine(_localizer["FFmpegSuccessPath", outputFile]);
        return new FFmpegConvertResult() { OutputPath = outputFile };
    }

    /// <summary>按时间段切割音频为多个片段</summary>
    private async Task<FFmpegSplitResult> SplitAudioAsync(FFmpegSplitPayload payload, CancellationToken ct)
    {
        if (!File.Exists(payload.InputFilePath))
            throw new FileNotFoundException(_localizer["FileNotFoundInput"], payload.InputFilePath);

        if (payload.Segments == null || payload.Segments.Count == 0)
            throw new ArgumentException(_localizer["SegmentsEmpty"], nameof(payload));

        var outputDir = payload.OutputDirectory;
        if (string.IsNullOrEmpty(outputDir))
            outputDir = Path.Combine(Environment.CurrentDirectory, "temp");

        if (!Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);

        var outputFiles = new List<string>();
        for (int i = 0; i < payload.Segments.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var (startMs, endMs) = payload.Segments[i];
            var startSec = (startMs / 1000.0).ToString("0.000", CultureInfo.InvariantCulture);
            var endSec = (endMs / 1000.0).ToString("0.000", CultureInfo.InvariantCulture);
            var outputFile = Path.Combine(outputDir, $"segment_{i + 1:D4}.wav");

            var args = $"-y -i \"{payload.InputFilePath}\" -ss {startSec} -to {endSec} " +
                       $"-ac 1 -ar 16000 -acodec pcm_s16le \"{outputFile}\"";

            using var process = StartProcess(args);
            ct.Register(() => { if (!process.HasExited) process.Kill(); });

            await process.WaitForExitAsync(ct);
            ct.ThrowIfCancellationRequested();

            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync();
                throw new FFmpegProcessExitException(
                    _localizer["FFmpegSplitFailed", i + 1, process.ExitCode],
                    process.ExitCode, error);
            }

            outputFiles.Add(outputFile);
            ConsoleServices.Output?.WriteLine(_localizer["FFmpegSplitSuccess", outputFile]);
        }

        return new FFmpegSplitResult { OutputFiles = outputFiles };
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
            throw new InvalidOperationException(_localizer["FailedStartFFmpegProcess"]);
        return process;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~FFmpegOperator() => Dispose(false);

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