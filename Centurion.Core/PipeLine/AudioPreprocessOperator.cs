using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Centurion.Core.Abstractions;
using Centurion.Core.Models;
using FFMpegCore;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.PipeLine;

public sealed class AudioPreprocessOperator(
    IBinaryLocator binaryLocator,
    ILogger<AudioPreprocessOperator> logger) : PipelineOperatorBase<AudioPreprocessOperator>(logger)
{
    private readonly IBinaryLocator _binaryLocator = binaryLocator ?? throw new ArgumentNullException(nameof(binaryLocator));

    public override string Name => "Audio Preprocessing";

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
            throw new FileNotFoundException($"Audio file not found: {inputPath}");
        if (string.IsNullOrWhiteSpace(tempDir))
            throw new InvalidOperationException("Pipeline temporary directory not set.");

        var config = context.Config.AudioPreprocess;
        var outputPath = Path.Combine(tempDir, $"preprocessed_{Guid.NewGuid():N}.wav");
        OnProgress(5, "Analysing audio for preprocessing...");

        context.State.SourceAudioInfo = await ProbeAsync(context.Config.InputFilePath, cancellationToken);
        var snr = 40.0;
        try
        {
            snr = WavSnrEstimator.Estimate(inputPath);
            context.State.EstimatedSnrDb = snr;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogWarning($"SNR estimation failed; using conservative default: {ex.Message}");
            context.State.EstimatedSnrDb = snr;
        }

        var useNoiseReduction = config.EnableNoiseReduction && snr < config.SnrThresholdDb && config.NoiseReductionBackend == AudioNoiseReductionBackend.BuiltInFfmpeg;
        context.State.NoiseReductionApplied = useNoiseReduction;
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
            context.State.PreprocessedAudioInfo = await ProbeAsync(outputPath, cancellationToken);
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
            LogError($"Audio preprocessing failed: {ex.Message}");
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

public sealed class AudioProbeException(string message, Exception? inner = null) : Exception(message, inner);

public sealed record LoudnormMeasurements(double InputIntegrated, double InputTruePeak, double InputLra, double InputThreshold, double TargetOffset);

public static class LoudnormJsonParser
{
    public static LoudnormMeasurements Parse(string output)
    {
        var start = output.LastIndexOf("{", StringComparison.Ordinal);
        var end = output.LastIndexOf("}", StringComparison.Ordinal);
        if (start < 0 || end <= start)
            throw new FormatException("loudnorm JSON was not found.");
        using var document = JsonDocument.Parse(output[start..(end + 1)]);
        var root = document.RootElement;
        return new LoudnormMeasurements(Read(root, "input_i"), Read(root, "input_tp"), Read(root, "input_lra"), Read(root, "input_thresh"), Read(root, "target_offset"));
    }

    private static double Read(JsonElement root, string name) => double.Parse(root.GetProperty(name).GetString()!, CultureInfo.InvariantCulture);
}

public static class WavSnrEstimator
{
    public static double Estimate(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        if (new string(reader.ReadChars(4)) != "RIFF") throw new FormatException("Only RIFF WAV is supported for SNR estimation.");
        reader.ReadInt32();
        if (new string(reader.ReadChars(4)) != "WAVE") throw new FormatException("Invalid WAV header.");
        short channels = 0, bits = 0;
        var sampleRate = 0;
        long dataStart = 0;
        var dataLength = 0;
        while (stream.Position + 8 <= stream.Length)
        {
            var id = new string(reader.ReadChars(4));
            var length = reader.ReadInt32();
            if (id == "fmt ")
            {
                if (reader.ReadInt16() != 1) throw new FormatException("WAV is not PCM.");
                channels = reader.ReadInt16();
                sampleRate = reader.ReadInt32();
                reader.ReadBytes(6);
                bits = reader.ReadInt16();
                reader.ReadBytes(Math.Max(0, length - 16));
            }
            else if (id == "data")
            {
                dataStart = stream.Position;
                dataLength = length;
                stream.Position += length;
            }
            else
                stream.Position += length;
            if ((length & 1) != 0) stream.Position++;
        }
        if (channels == 0 || bits != 16 || dataStart == 0 || dataLength == 0 || sampleRate == 0)
            throw new FormatException("Unsupported WAV format.");
        stream.Position = dataStart;
        var samplesPerFrame = Math.Max(1, sampleRate / 20);
        var frameRms = new List<double>();
        var frameSquares = new List<double>(samplesPerFrame);
        for (var i = 0; i < dataLength / 2; i++)
        {
            frameSquares.Add(reader.ReadInt16() / 32768.0);
            if (frameSquares.Count < samplesPerFrame && i + 1 < dataLength / 2) continue;
            frameRms.Add(Math.Sqrt(frameSquares.Select(value => value * value).Average()));
            frameSquares.Clear();
        }
        if (frameRms.Count < 2) return 40;
        frameRms.Sort();
        var noise = frameRms.Take(Math.Max(1, frameRms.Count / 5)).Select(value => value * value).Average();
        var signal = frameRms.Skip(frameRms.Count / 2).Select(value => value * value).Average();
        return 10 * Math.Log10(Math.Max(signal, 1e-12) / Math.Max(noise, 1e-12));
    }
}
