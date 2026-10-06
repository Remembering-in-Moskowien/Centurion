using System;
using System.Buffers;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Centurion.Abstractions;
using Centurion.Abstractions.Exceptions;
using Centurion.Core.Capabilities.Managers.Runtime;
using Centurion.Core.Infrastructure;
using Centurion.Core.Operators.Download;
using Centurion.Core.Operators.Download.Request;
using Centurion.Models.Workflow;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Centurion.Core.Workflow.Strategy.VocalSeparation;

/// <summary>
/// Native htdemucs vocal separation via ONNX Runtime (no python, no external CLI).
/// The MIT-licensed StemSplitio ONNX graphs take a fixed (1, 2, 343980) float32 mix segment
/// (7.8 s @ 44.1 kHz stereo) and emit (1, sources, 2, 343980) stem waveforms; longer audio is
/// chunked with quarter-segment overlap-add and a triangular transition window. Input audio is
/// converted to 44.1 kHz stereo float PCM through ffmpeg (located via IBinaryLocator); output is
/// written as a 44.1 kHz stereo 16-bit WAV.
/// </summary>
public sealed class HtDemucsOnnxVocalSeparator
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger _logger;

    /// <summary>Set once DirectML fails at run time; subsequent runs skip the GPU attempt.</summary>
    private static int _directMlFailed;

    /// <summary>Models are cached under the app's models/htdemucs directory.</summary>
    private const string ModelDirectoryName = "htdemucs";

    /// <summary>Resolves DI dependencies (downloader, ffmpeg locator, process manager) and the logger.</summary>
    public HtDemucsOnnxVocalSeparator(IServiceProvider serviceProvider, ILogger<HtDemucsOnnxVocalSeparator> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Separates the vocals stem of <paramref name="audioPath"/> and writes a 44.1 kHz stereo
    /// 16-bit WAV to <paramref name="outputWavPath"/>. Downloads the ONNX model on first use.
    /// </summary>
    /// <returns>The output path.</returns>
    public async Task<string> SeparateVocalsAsync(
        string audioPath,
        string outputWavPath,
        string model,
        InferenceDevice device,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(audioPath) || !File.Exists(audioPath))
            throw new FileNotFoundException($"Audio file not found: {audioPath}");
        if (string.IsNullOrWhiteSpace(outputWavPath))
            throw new ArgumentException("Output path is required.", nameof(outputWavPath));

        var repo = HtDemucsOnnxEngine.GetOnnxModelInfo(model, out var fileName, out var sources);
        if (repo is null)
            throw new InvalidDataException($"Unknown htdemucs model '{model}'. Supported: htdemucs, htdemucs_6s.");

        // 1. Model (auto-download through the HF mirror chain on first use).
        var modelPath = await EnsureModelAsync(repo, fileName, cancellationToken);

        // 2. Convert input to 44.1 kHz stereo float PCM (ffmpeg).
        var pcmPath = Path.Combine(Path.GetTempPath(), $"htdemucs-in-{Guid.NewGuid():N}.f32");
        try
        {
            await ConvertToFloatPcmAsync(audioPath, pcmPath, cancellationToken);

            // 3. Read samples (channel-interleaved) and separate.
            var mix = await ReadFloatPcmAsync(pcmPath, cancellationToken);
            var vocals = InferVocals(modelPath, mix, device, cancellationToken);

            // 4. Write output WAV.
            WritePcm16StereoWav(outputWavPath, vocals, HtDemucsOnnxEngine.SampleRate);
            return outputWavPath;
        }
        finally
        {
            TryDelete(pcmPath);
        }
    }

    // ---------- Model ----------

    private async Task<string> EnsureModelAsync(string repo, string fileName, CancellationToken cancellationToken)
    {
        // Content-addressed storage through the shared ModelManager: the model lands as
        // models/htdemucs/<sha256>.onnx and the category manifest maps "htdemucs" to its hash.
        var meta = new Centurion.Models.Metadata.ModelMeta(
            fileName, HtDemucsOnnxEngine.BuildModelUrl(repo, fileName));
        var dict = new Dictionary<string, Centurion.Models.Metadata.ModelMeta>(StringComparer.OrdinalIgnoreCase)
        {
            ["htdemucs"] = meta
        };

        using var manager = new Centurion.Core.Capabilities.Managers.Media.ModelManager(
            "htdemucs", dict, _serviceProvider, ModelDirectoryName);
        await manager.EnsureInstalledAsync(cancellationToken);
        if (string.IsNullOrEmpty(manager.ModelFilePath))
            throw new InvalidDataException("htdemucs model download failed (no path resolved).");
        _logger.LogInformation("htdemucs ONNX model ready: {Path} ({Hash})", manager.ModelFilePath, manager.InstalledHash);
        return manager.ModelFilePath;
    }

    // ---------- Audio I/O ----------

    private async Task ConvertToFloatPcmAsync(string inputPath, string outputPath, CancellationToken cancellationToken)
    {
        var ffmpeg = ResolveFfmpeg();
        if (ffmpeg is null)
            throw new InvalidOperationException(
                "ffmpeg is required for htdemucs ONNX vocal separation (input resampling to 44.1 kHz stereo). Install it or add it under tools/ffmpeg.");

        var processManager = _serviceProvider.GetRequiredService<ProcessManager>();
        var args = new[]
        {
            "-y", "-i", inputPath,
            "-ac", "2", "-ar", HtDemucsOnnxEngine.SampleRate.ToString(),
            "-f", "f32le", outputPath
        };
        await processManager.ExecuteAsync(ffmpeg, args, cancellationToken);
        if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
            throw new InvalidDataException($"ffmpeg produced no PCM output for {inputPath}");
    }

    private static async Task<float[]> ReadFloatPcmAsync(string path, CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        if (bytes.Length == 0 || bytes.Length % sizeof(float) != 0)
            throw new InvalidDataException($"Invalid float PCM payload: {bytes.Length} bytes");
        var count = bytes.Length / sizeof(float);
        var samples = new float[count];
        Buffer.BlockCopy(bytes, 0, samples, 0, bytes.Length);
        return samples;
    }

    private static void WritePcm16StereoWav(string path, float[] interleavedStereo, int sampleRate)
    {
        if (interleavedStereo.Length % HtDemucsOnnxEngine.ChannelCount != 0)
            throw new InvalidDataException("Vocal output length is not stereo-interleaved.");
        var frames = interleavedStereo.Length / HtDemucsOnnxEngine.ChannelCount;
        var dataSize = frames * HtDemucsOnnxEngine.ChannelCount * 2;

        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataSize);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);   // PCM
        writer.Write((short)2);   // stereo
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2 * 2); // byte rate
        writer.Write((short)4);   // block align
        writer.Write((short)16);  // bits per sample
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataSize);

        foreach (var s in interleavedStereo)
        {
            var clamped = Math.Clamp(s, -1f, 1f);
            writer.Write((short)(clamped * 32767f));
        }
    }

    private string? ResolveFfmpeg()
    {
        try
        {
            return _serviceProvider.GetService<IBinaryLocator>()?.Locate(
                OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg", "tools", "ffmpeg");
        }
        catch (BinaryNotFoundException)
        {
            return null;
        }
    }

    // ---------- Inference ----------

    /// <summary>
    /// Runs the model over fixed-size chunks with quarter-segment overlap-add and returns the
    /// vocals stem as interleaved stereo float samples. When a GPU execution provider is preferred
    /// but fails at run time (e.g. DirectML out-of-memory on low-memory machines), the inference is
    /// retried on a CPU-only session.
    /// </summary>
    private float[] InferVocals(
        string modelPath, float[] mix, InferenceDevice device, CancellationToken cancellationToken)
    {
        var frames = mix.Length / HtDemucsOnnxEngine.ChannelCount;
        var channels = new float[HtDemucsOnnxEngine.ChannelCount][];
        for (var c = 0; c < HtDemucsOnnxEngine.ChannelCount; c++)
        {
            channels[c] = new float[frames];
            for (var i = 0; i < frames; i++)
                channels[c][i] = mix[i * HtDemucsOnnxEngine.ChannelCount + c];
        }

        var nChunks = HtDemucsOnnxEngine.ComputeChunkCount(frames);
        var stride = HtDemucsOnnxEngine.SegmentSamples - HtDemucsOnnxEngine.Overlap;
        var window = HtDemucsOnnxEngine.BuildTransitionWindow();

        // Once DirectML fails at run time (e.g. out of memory on low-memory machines), remember it
        // for the process so later runs skip the doomed GPU attempt and its error spam.
        var tryGpu = device != InferenceDevice.Cpu && Volatile.Read(ref _directMlFailed) == 0;
        if (!tryGpu)
        {
            using var cpuSession = new InferenceSession(
                modelPath, OnnxSessionFactory.CreateSessionOptions(preferGpu: false));
            return RunChunks(cpuSession, channels, frames, nChunks, stride, window, cancellationToken);
        }

        try
        {
            using var session = new InferenceSession(
                modelPath, OnnxSessionFactory.CreateSessionOptions(device != InferenceDevice.Cpu));
            return RunChunks(session, channels, frames, nChunks, stride, window, cancellationToken);
        }
        catch (OnnxRuntimeException ex) when (device != InferenceDevice.Cpu)
        {
            Interlocked.Exchange(ref _directMlFailed, 1);
            _logger.LogWarning(
                "htdemucs GPU (DirectML) inference failed; retrying on CPU. {Message}", ex.Message);
            using var cpuSession = new InferenceSession(
                modelPath, OnnxSessionFactory.CreateSessionOptions(preferGpu: false));
            return RunChunks(cpuSession, channels, frames, nChunks, stride, window, cancellationToken);
        }
    }

    private static float[] RunChunks(
        InferenceSession session,
        float[][] channels,
        int frames,
        int nChunks,
        int stride,
        float[] window,
        CancellationToken cancellationToken)
    {
        var outVocals = new float[HtDemucsOnnxEngine.ChannelCount * frames];
        var weight = new float[frames];
        var inputTensor = new DenseTensor<float>(new[] { 1, HtDemucsOnnxEngine.ChannelCount, HtDemucsOnnxEngine.SegmentSamples });
        var outputs = new[] { "stems" };

        for (var i = 0; i < nChunks; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var start = i * stride;
            var end = Math.Min(start + HtDemucsOnnxEngine.SegmentSamples, frames);
            var chunkLen = end - start;

            // Build the (1, 2, 343980) input: pad the tail with zeros.
            for (var t = 0; t < HtDemucsOnnxEngine.SegmentSamples; t++)
            {
                var src = start + t < frames ? start + t : frames - 1;
                var fill = start + t < frames ? 1f : 0f;
                inputTensor[0, 0, t] = channels[0][src] * fill;
                inputTensor[0, 1, t] = channels[1][src] * fill;
            }

            using var results = session.Run(new[] { NamedOnnxValue.CreateFromTensor("mix", inputTensor) }, outputs);
            var stems = results[0].AsTensor<float>(); // (1, S, 2, N)

            // vocals row = index 3 (drums, bass, other, vocals).
            var vocals = HtDemucsOnnxEngine.VocalsIndex;
            for (var c = 0; c < HtDemucsOnnxEngine.ChannelCount; c++)
            {
                var wavBase = c * frames + start;
                for (var t = 0; t < chunkLen; t++)
                    outVocals[wavBase + t] += stems[0, vocals, c, t] * window[t];
            }
            for (var t = 0; t < chunkLen; t++)
                weight[start + t] += window[t];
        }

        // Normalize by the accumulated window weights.
        for (var c = 0; c < HtDemucsOnnxEngine.ChannelCount; c++)
        {
            var baseIdx = c * frames;
            for (var i = 0; i < frames; i++)
            {
                var w = Math.Max(weight[i], 1e-8f);
                outVocals[baseIdx + i] /= w;
            }
        }
        return outVocals;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best effort only.
        }
    }
}
