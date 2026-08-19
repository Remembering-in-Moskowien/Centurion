using System.Diagnostics;
using System.Globalization;
using System.Text;
using Centurion.Core.Abstractions;
using Centurion.Core.Exceptions;
using Centurion.Core.Request;
using Centurion.Core.Response;
using Centurion.Core.Tools;

namespace Centurion.Core.Operators;

/// <summary>
/// FFmpeg 音频分割算子（按时间段切割，自动在前后添加 100ms 静音）
/// </summary>
public class FFmpegSplitOperator(IBinaryLocator binaryLocator)
    : IOperator<FFmpegSplitRequest, FFmpegSplitResponse>, IDisposable
{
    private string? _ffmpegBinaryPath;
    private Process? _runningProcess;
    private bool _disposed;

    // 静音填充时长（毫秒）
    private const int SilencePaddingMs = 100;

    public async Task EnsureTargetAvailableAsync()
    {
        if (!string.IsNullOrEmpty(_ffmpegBinaryPath) && File.Exists(_ffmpegBinaryPath))
            return;

        var bin = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        _ffmpegBinaryPath = binaryLocator.Locate(bin, "ffmpeg");
        if (string.IsNullOrEmpty(_ffmpegBinaryPath) || !File.Exists(_ffmpegBinaryPath))
            throw new FileNotFoundException("FFmpeg executable not found.");
    }

    public async Task<FFmpegSplitResponse> ProcessAsync(
        OperatorsRequest<FFmpegSplitRequest> request,
        CancellationToken cancellationToken = default)
    {
        await EnsureTargetAvailableAsync();
        return await SplitAudioAsync(request.Payload, cancellationToken);
    }

    private async Task<FFmpegSplitResponse> SplitAudioAsync(FFmpegSplitRequest payload, CancellationToken ct)
    {
        if (!File.Exists(payload.InputFilePath))
            throw new FileNotFoundException("Input file not found", payload.InputFilePath);

        if (payload.Segments == null || payload.Segments.Count == 0)
            throw new ArgumentException("Segment list cannot be empty.", nameof(payload));

        // 获取输入音频总时长（用于边界检查）
        var totalDuration = await GetAudioDurationAsync(payload.InputFilePath, ct);

        List<string> outputFiles;
        if (payload.OutputFileNames != null && payload.OutputFileNames.Count == payload.Segments.Count)
        {
            outputFiles = payload.OutputFileNames;
        }
        else
        {
            var outputDir = payload.OutputDirectory ?? Path.Combine(Environment.CurrentDirectory, "temp");
            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);
            outputFiles = [];
            for (int i = 0; i < payload.Segments.Count; i++)
                outputFiles.Add(Path.Combine(outputDir, $"segment_{i + 1:D4}.wav"));
        }

        foreach (var file in outputFiles)
        {
            var dir = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }

        var results = new List<string>();
        for (var i = 0; i < payload.Segments.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var (startMs, endMs) = payload.Segments[i];
            var rawDurationSec = (endMs - startMs) / 1000.0;

            // 边界检查：确保 start/end 不超出音频总时长
            var safeStartMs = Math.Max(0, startMs);
            var safeEndMs = Math.Min(endMs, totalDuration * 1000);
            if (safeStartMs >= safeEndMs)
                throw new InvalidOperationException($"Invalid segment: start {safeStartMs} >= end {safeEndMs}");

            // 转换为秒（ffmpeg 需要浮点数）
            var startSec = (safeStartMs / 1000.0).ToString("0.000", CultureInfo.InvariantCulture);
            var endSec = (safeEndMs / 1000.0).ToString("0.000", CultureInfo.InvariantCulture);
            var outputFile = outputFiles[i];

            // 构造 filter：开头加 100ms 静音，末尾补足到总时长（原始时长 + 0.2s）
            var totalDurationWithPad = rawDurationSec + (2.0 * SilencePaddingMs / 1000.0);
            var filterComplex =
                $"[0:a]adelay={SilencePaddingMs}|{SilencePaddingMs},apad=pad_dur={totalDurationWithPad:0.000}[a]";

            var args = $"-y -i \"{payload.InputFilePath}\" -ss {startSec} -to {endSec} " +
                       $"-filter_complex \"{filterComplex}\" -map \"[a]\" " +
                       $"-ac 1 -ar 16000 -acodec pcm_s16le \"{outputFile}\"";

            using var process = StartProcess(args);
            ct.Register(() =>
            {
                if (!process.HasExited) process.Kill();
            });

            await process.WaitForExitAsync(ct);
            ct.ThrowIfCancellationRequested();

            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync();
                throw new FFmpegProcessExitException(
                    $"Segment {i + 1} split failed with exit code {process.ExitCode}.",
                    process.ExitCode, error);
            }

            results.Add(outputFile);
            ConsoleServices.Output?.WriteLine($"Segment saved (with 100ms silence padding): {outputFile}");
        }

        return new FFmpegSplitResponse { OutputFiles = results };
    }

    /// <summary>
    /// 获取音频时长（秒）
    /// </summary>
    private async Task<double> GetAudioDurationAsync(string filePath, CancellationToken ct)
    {
        var args = $"-i \"{filePath}\" -f null -";
        using var process = StartProcess(args);
        var errorOutput = new StringBuilder();

        process.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                errorOutput.AppendLine(e.Data);
        };
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(ct);
        if (process.ExitCode != 0)
            throw new Exception($"Failed to probe audio duration: {errorOutput}");

        // 解析输出中的 Duration: HH:MM:SS.mm
        var output = errorOutput.ToString();
        var match = System.Text.RegularExpressions.Regex.Match(output,
            @"Duration:\s*(\d{2}):(\d{2}):(\d{2})\.(\d{2,3})");
        if (!match.Success)
            throw new Exception("Could not parse audio duration from ffprobe output.");

        var hours = int.Parse(match.Groups[1].Value);
        var minutes = int.Parse(match.Groups[2].Value);
        var seconds = int.Parse(match.Groups[3].Value);
        var milliseconds = int.Parse(match.Groups[4].Value.PadRight(3, '0'));

        var duration = new TimeSpan(0, hours, minutes, seconds, milliseconds).TotalSeconds;
        return duration;
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

    ~FFmpegSplitOperator()
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