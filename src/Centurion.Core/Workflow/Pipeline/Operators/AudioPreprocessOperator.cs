using System.Diagnostics;
using System.Text;
using Centurion.Abstractions;
using Centurion.Abstractions.Pipeline;
using Centurion.Abstractions.Exceptions;
using Centurion.Models;
using Centurion.Models.Workflow;
using FFMpegCore;
using Microsoft.Extensions.Logging;
using Centurion.Core.Utils.Media;
using Centurion.Core.Utils.Parsing;
namespace Centurion.Core.Workflow.Pipeline.Operators;

/// <summary>
/// 音频预处理算子：估算信噪比、按需降噪与响度归一化，并重采样为下游处理所需格式，
/// 结果写入 <see cref="SubtitleWorkflowContext"/> 状态中的 PreprocessedAudioPath。
/// </summary>
public sealed class AudioPreprocessOperator(
    IBinaryLocator binaryLocator,
    ILogger<AudioPreprocessOperator> logger) : PipelineOperatorBase<AudioPreprocessOperator>(logger)
{
    private readonly IBinaryLocator _binaryLocator = binaryLocator ?? throw new ArgumentNullException(nameof(binaryLocator));

    /// <summary>算子在管道中的显示名称。</summary>
    public override string Name => "Audio Preprocessing";

    /// <summary>
    /// 执行音频预处理：探测音频信息、估算信噪比，按配置应用降噪与响度归一化，
    /// 输出预处理后的音频文件并写回工作流状态。
    /// </summary>
    /// <param name="context">字幕工作流上下文，提供配置与输入音频路径。</param>
    /// <param name="cancellationToken">用于取消预处理过程的取消标记。</param>
    public override async Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken)
    {
        if (context.State.PreprocessedAudioPath is { } existing && File.Exists(existing))
        {
            LogInfo("Audio preprocessing already exists, skipping.");
            return;
        }

                var inputPath = context.State.ConvertedAudioPath ?? context.Config.InputFilePath;
        var tempDir = context.State.PipelineTempDirectory;
        if (!File.Exists(inputPath))
        {
            // 跨命令运行时，先前 transcribe 的临时音频可能已被清理；
            // 回退到源媒体重新预处理，保证 align/correct 等命令可独立使用。
            LogWarning($"Converted/preprocessed audio not found ({inputPath}); falling back to source media.");
            inputPath = context.Config.InputFilePath;
        }
        if (!File.Exists(inputPath))
            throw new FileNotFoundException($"Audio file not found: {inputPath}");
        if (string.IsNullOrWhiteSpace(tempDir))
            throw new InvalidOperationException("Pipeline temporary directory not set.");
        Directory.CreateDirectory(tempDir);

        var config = context.Config.AudioPreprocess;
        var outputPath = Path.Combine(tempDir, $"preprocessed_{Guid.NewGuid():N}.wav");
        OnProgress(5, "Analysing audio for preprocessing...");

        context.State.Extensions["SourceAudioInfo"] = await ProbeAsync(context.Config.InputFilePath, cancellationToken);
        var snr = 40.0;
        try
        {
            snr = WavSnrEstimator.Estimate(inputPath);
            context.State.Extensions["EstimatedSnrDb"] = snr;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogWarning($"SNR estimation failed; using conservative default: {ex.Message}");
            context.State.Extensions["EstimatedSnrDb"] = snr;
        }

        var useNoiseReduction = config.EnableNoiseReduction && snr < config.SnrThresholdDb && config.NoiseReductionBackend == AudioNoiseReductionBackend.BuiltInFfmpeg;
        context.State.Extensions["NoiseReductionApplied"] = useNoiseReduction;
        if (config.EnableNoiseReduction && snr < config.SnrThresholdDb && config.NoiseReductionBackend == AudioNoiseReductionBackend.ExternalCli)
            LogWarning("External CLI noise reduction backend is reserved for a future implementation; continuing without noise reduction.");
        LogInfo($"Estimated SNR: {snr:F1} dB; noise reduction: {(useNoiseReduction ? "enabled" : "disabled")}.");

        LoudnormMeasurements? measurements = null;
        if (config.EnableLoudnessNormalization)
        {
            try
            {
                var analysis = await RunFfmpegAsync(inputPath, null, BuildFilter(config, useNoiseReduction, true), cancellationToken);
                measurements = LoudnormJsonParser.Parse(analysis.StandardError);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogWarning($"Loudness analysis failed; using one-pass normalization: {ex.Message}");
            }
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var filter = BuildFilter(config, useNoiseReduction, false, measurements);
            await RunFfmpegAsync(inputPath, outputPath, filter, cancellationToken);
            context.State.PreprocessedAudioPath = outputPath;
            context.State.Extensions["PreprocessedAudioInfo"] = await ProbeAsync(outputPath, cancellationToken);
            OnProgress(100, "Audio preprocessing completed.");
            LogInfo($"Preprocessed audio saved to: {outputPath}");
        }
        catch (OperationCanceledException)
        {
            TryDelete(outputPath);
            throw;
        }
        catch (Exception ex)
        {
            TryDelete(outputPath);
            LogWarning($"Audio preprocessing failed: {ex.Message}");
            throw new AudioConversionException($"Audio preprocessing failed for '{inputPath}'", ex);
        }
    }

    private static string BuildFilter(AudioPreprocessConfig config, bool noiseReduction, bool analysis, LoudnormMeasurements? measurements = null)
    {
        var filters = new List<string>();
        if (config.EnableResampling)
            filters.Add("aresample=osr=16000:filter_size=256:cutoff=0.8");
        if (config.EnableHighPass)
            filters.Add("highpass=f=100");
        if (noiseReduction && config.NoiseReductionBackend == AudioNoiseReductionBackend.BuiltInFfmpeg)
            filters.Add("afftdn");
        if (config.EnableLoudnessNormalization)
        {
            var loudnorm = "loudnorm=I=-20:TP=-1.5:LRA=11:dual_mono=true";
            if (analysis)
                loudnorm += ":print_format=json";
            else if (measurements is not null)
                loudnorm += $":measured_I={measurements.InputIntegrated:F2}:measured_TP={measurements.InputTruePeak:F2}:measured_LRA={measurements.InputLra:F2}:measured_thresh={measurements.InputThreshold:F2}:offset={measurements.TargetOffset:F2}:linear=true:print_format=summary";
            filters.Add(loudnorm);
        }
        return string.Join(',', filters);
    }

    private async Task<AudioProbeInfo> ProbeAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var info = await FFProbe.AnalyseAsync(path, cancellationToken: cancellationToken);
            var stream = info.AudioStreams.FirstOrDefault() ?? throw new AudioProbeException($"No audio stream found: {path}");
            return new AudioProbeInfo(stream.SampleRateHz, stream.Channels, stream.CodecName ?? "unknown", info.Format.FormatName ?? "unknown");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new AudioProbeException($"Unable to read audio: {path}", ex);
        }
    }

    private async Task<FfmpegResult> RunFfmpegAsync(string input, string? output, string filter, CancellationToken cancellationToken)
    {
        var executable = _binaryLocator.Locate(OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg", "tools", "ffmpeg");
        var outputArgument = output is null
            ? (OperatingSystem.IsWindows() ? "-f null NUL" : "-f null /dev/null")
            : $"-map_metadata -1 -acodec pcm_s16le -ar 16000 -ac 1 -y {Quote(output)}";
        var arguments = $"-hide_banner -i {Quote(input)} -vn -af {Quote(filter)} {outputArgument}";
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            }
        };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };
        if (!process.Start())
            throw new InvalidOperationException("Unable to start FFmpeg.");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        using var registration = cancellationToken.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(true); } catch { }
        });
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"FFmpeg failed with exit code {process.ExitCode}: {stderr}");
        return new FfmpegResult(stdout.ToString(), stderr.ToString());
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private sealed record FfmpegResult(string StandardOutput, string StandardError);
}
