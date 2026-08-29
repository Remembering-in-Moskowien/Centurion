using Centurion.Core.Abstractions;
using Centurion.Core.Exceptions;
using Centurion.Core.Managers;
using Centurion.Core.Operators.Request;
using Centurion.Core.Operators.Response;
using FFMpegCore;

namespace Centurion.Core.Operators;

/// <summary>
/// FFmpeg 音频分割算子（按时间段切割，自动在前后添加 100ms 静音）
/// </summary>
public class FFmpegSpliter : IOperator<FFmpegSplitRequest, FFmpegSplitResponse>
{
    private readonly FFmpegManager _ffmpegManager;
    private const int SilencePaddingMs = 100;

    public FFmpegSpliter(FFmpegManager ffmpegManager)
    {
        _ffmpegManager = ffmpegManager;
    }

    public async Task CheckHealthAsync()
    {
        await _ffmpegManager.CheckHealthAsync();
    }

    public async Task<FFmpegSplitResponse> ProcessAsync(
        OperatorsRequest<FFmpegSplitRequest> request,
        CancellationToken cancellationToken = default)
    {
        await CheckHealthAsync();
        return await SplitAudioAsync(request.Payload, cancellationToken);
    }

    private async Task<FFmpegSplitResponse> SplitAudioAsync(FFmpegSplitRequest payload, CancellationToken ct)
    {
        if (!File.Exists(payload.InputFilePath))
            throw new FileNotFoundException("Input file not found", payload.InputFilePath);
        if (payload.Segments == null || payload.Segments.Count == 0)
            throw new ArgumentException("Segment list cannot be empty.", nameof(payload));

        // 获取总时长（使用 FFMpegCore 的 MediaInfo）
        var mediaInfo = await FFProbe.AnalyseAsync(payload.InputFilePath, cancellationToken: ct);
        var totalDuration = mediaInfo.Duration.TotalSeconds;

        // 生成输出文件名列表
        List<string> outputFiles;
        if (payload.OutputFileNames != null && payload.OutputFileNames.Count == payload.Segments.Count)
        {
            outputFiles = payload.OutputFileNames;
        }
        else
        {
            var outputDir = payload.OutputDirectory ?? Path.Combine(Environment.CurrentDirectory, "temp");
            if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);
            outputFiles = [];
            for (var i = 0; i < payload.Segments.Count; i++)
                outputFiles.Add(Path.Combine(outputDir, $"segment_{i + 1:D4}.wav"));
        }

        // 确保输出目录存在
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

            // 边界检查
            var safeStartMs = Math.Max(0, startMs);
            var safeEndMs = Math.Min(endMs, totalDuration * 1000);
            if (safeStartMs >= safeEndMs)
                throw new InvalidOperationException($"Invalid segment: start {safeStartMs} >= end {safeEndMs}");

            var startSec = safeStartMs / 1000.0;
            var endSec = safeEndMs / 1000.0;
            var outputFile = outputFiles[i];

            // 构造 filter 字符串（与原有逻辑一致）
            var totalDurationWithPad = rawDurationSec + 2.0 * SilencePaddingMs / 1000.0;
            var filterComplex =
                $"[0:a]adelay={SilencePaddingMs}|{SilencePaddingMs},apad=pad_dur={totalDurationWithPad:0.000}[a]";

            // 使用 FFMpegCore，将过滤器和裁剪参数组合
            await FFMpegArguments
                .FromFileInput(payload.InputFilePath)
                .OutputToFile(outputFile, false, options =>
                {
                    options
                        .WithCustomArgument($"-ss {startSec:0.000} -to {endSec:0.000}")
                        .WithCustomArgument($"-filter_complex \"{filterComplex}\"")
                        .WithCustomArgument("-map \"[a]\"")
                        .WithAudioCodec("pcm_s16le")
                        .WithAudioSamplingRate(16000)
                        .WithCustomArgument("-ac 1")
                        .ForceFormat("wav");
                })
                .ProcessAsynchronously();

            results.Add(outputFile);
            ConsoleServices.Output?.WriteLine($"Segment saved (with 100ms silence padding): {outputFile}");
        }

        return new FFmpegSplitResponse { OutputFiles = results };
    }

    public void Dispose()
    {
        Dispose(true);
    }

    protected virtual void Dispose(bool disposing)
    {
    }
}