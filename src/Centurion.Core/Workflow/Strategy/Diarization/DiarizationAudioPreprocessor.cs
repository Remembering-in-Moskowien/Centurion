using System.Text;
using Centurion.Core.Capabilities.Managers.Runtime;

namespace Centurion.Core.Workflow.Strategy.Diarization;

/// <summary>
/// Shared audio preprocessing for external diarization engines (polyvoice / sherpa-onnx):
/// both require 16 kHz mono PCM WAV input. Prefers ffmpeg when it can be located (via
/// <see cref="Centurion.Abstractions.IBinaryLocator"/>), and falls back to a pure managed
/// linear-interpolation resampler so the feature works with zero extra dependencies.
/// </summary>
public static class DiarizationAudioPreprocessor
{
    /// <summary>Target sample rate required by polyvoice and sherpa-onnx.</summary>
    public const int TargetSampleRate = 16000;

    /// <summary>
    /// Ensures the audio is a 16 kHz mono WAV, converting when needed. Returns the original path
    /// when already compatible; otherwise writes a converted file next to the source (same stem +
    /// ".16k.wav") so callers can trace the artifact.
    /// </summary>
    /// <param name="audioPath">Path to the source audio (any PCM WAV).</param>
    /// <param name="binaryLocator">Optional binary locator used to find ffmpeg; null skips ffmpeg.</param>
    /// <param name="processManager">Optional process manager used to run ffmpeg; ignored when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Path to a 16 kHz mono WAV.</returns>
    public static async Task<string> Ensure16KHzMonoAsync(
        string audioPath,
        Centurion.Abstractions.IBinaryLocator? binaryLocator,
        ProcessManager? processManager,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(audioPath) || !File.Exists(audioPath))
            throw new FileNotFoundException($"Audio file not found: {audioPath}");

        var info = ProbeWav(audioPath);
        if (info is null)
            throw new InvalidDataException($"Not a supported PCM WAV file: {audioPath}");
        if (info.SampleRate == TargetSampleRate && info.Channels == 1)
            return audioPath;

        var output = Path.Combine(
            Path.GetDirectoryName(audioPath) ?? string.Empty,
            Path.GetFileNameWithoutExtension(audioPath) + ".16k.wav");

        // 1. Prefer ffmpeg (accurate resampling + any codec).
        var ffmpeg = ResolveFfmpeg(binaryLocator);
        if (ffmpeg is not null && processManager is not null)
        {
            var args = new[]
            {
                "-y", "-i", audioPath,
                "-ar", TargetSampleRate.ToString(), "-ac", "1",
                "-c:a", "pcm_s16le", output
            };
            await processManager.ExecuteAsync(ffmpeg, args, cancellationToken);
            return output;
        }

        // 2. Managed fallback: linear-interpolation resample of PCM16 / IEEE float mono/stereo.
        WriteResampledWav(audioPath, info, output);
        return output;
    }

    /// <summary>Basic RIFF/PCM WAV header probe (PCM16 or IEEE float; mono/stereo).</summary>
    private static WavInfo? ProbeWav(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        if (stream.Length < 44)
            return null;

        var riff = new string(reader.ReadChars(4));
        if (riff != "RIFF")
            return null;
        reader.ReadInt32(); // riff size
        var wave = new string(reader.ReadChars(4));
        if (wave != "WAVE")
            return null;

        int? sampleRate = null, channels = null, bits = null, format = null;
        long dataSize = -1;

        while (stream.Position + 8 <= stream.Length)
        {
            var chunkId = new string(reader.ReadChars(4));
            var chunkSize = reader.ReadInt32();
            if (chunkSize < 0 || stream.Position + chunkSize > stream.Length)
                return null;

            switch (chunkId)
            {
                case "fmt " when chunkSize >= 16:
                    format = reader.ReadInt16();
                    channels = reader.ReadInt16();
                    sampleRate = reader.ReadInt32();
                    reader.ReadInt32(); // byte rate
                    reader.ReadInt16(); // block align
                    bits = reader.ReadInt16();
                    // fmt may carry extension bytes; skip whatever remains of this chunk.
                    stream.Seek(chunkSize - 16, SeekOrigin.Current);
                    continue;
                case "data":
                    dataSize = chunkSize;
                    break;
                default:
                    break;
            }

            // fmt always precedes data; once data is reached the header is complete.
            if (chunkId == "data")
                break;
            stream.Seek(chunkSize, SeekOrigin.Current);
        }

        if (sampleRate is null || channels is null || bits is null || format is null || dataSize < 0)
            return null;
        return new WavInfo(sampleRate.Value, channels.Value, bits.Value, format.Value, dataSize);
    }

    /// <summary>Resolves ffmpeg via the binary locator, tolerating absence.</summary>
    private static string? ResolveFfmpeg(Centurion.Abstractions.IBinaryLocator? binaryLocator)
    {
        if (binaryLocator is null)
            return null;
        try
        {
            return binaryLocator.Locate(OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg", "tools", "ffmpeg");
        }
        catch (Centurion.Abstractions.Exceptions.BinaryNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Managed resampler: reads the source samples, linearly interpolates to 16 kHz, writes PCM16 mono.</summary>
    private static void WriteResampledWav(string input, WavInfo info, string output)
    {
        var samples = ReadSamples(input, info);
        if (samples.Length == 0)
            throw new InvalidDataException($"No audio samples found in {input}");

        var ratio = (double)TargetSampleRate / info.SampleRate;
        var outCount = (int)Math.Ceiling(samples.Length * ratio);
        var resampled = new float[outCount];
        for (var i = 0; i < outCount; i++)
        {
            var srcPos = i / ratio;
            var i0 = (int)srcPos;
            var i1 = Math.Min(i0 + 1, samples.Length - 1);
            var frac = srcPos - i0;
            resampled[i] = samples[i0] * (1f - (float)frac) + samples[i1] * (float)frac;
        }

        WritePcm16Mono(output, resampled, TargetSampleRate);
    }

    /// <summary>Decodes the whole WAV into interleaved mono float samples (-1..1), downmixing stereo by averaging.</summary>
    private static float[] ReadSamples(string path, WavInfo info)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);

        // Re-locate the data chunk (ProbeWav leaves the stream at its start).
        stream.Position = 12;
        long dataOffset = -1, dataSize = -1;
        while (stream.Position + 8 <= stream.Length)
        {
            var id = new string(reader.ReadChars(4));
            var size = reader.ReadInt32();
            if (id == "data") { dataOffset = stream.Position; dataSize = size; break; }
            stream.Seek(size, SeekOrigin.Current);
        }
        if (dataOffset < 0 || dataSize < 0)
            throw new InvalidDataException($"WAV has no data chunk: {path}");

        stream.Position = dataOffset;
        var frameBytes = (info.BitsPerSample / 8) * info.Channels;
        var frames = (int)(dataSize / frameBytes);
        var mono = new float[frames];

        for (var i = 0; i < frames; i++)
        {
            double sum = 0;
            for (var c = 0; c < info.Channels; c++)
            {
                var sample = info.BitsPerSample switch
                {
                    16 => reader.ReadInt16() / 32768.0,
                    32 when info.FormatTag == 3 => reader.ReadSingle(),
                    _ => throw new NotSupportedException($"WAV format {info.FormatTag}/{info.BitsPerSample}-bit is not supported for managed resampling; install ffmpeg to convert it.")
                };
                sum += sample;
            }
            mono[i] = (float)(sum / info.Channels);
        }

        return mono;
    }

    /// <summary>Writes a PCM16 mono WAV at the given sample rate.</summary>
    private static void WritePcm16Mono(string path, float[] samples, int sampleRate)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);

        var dataSize = samples.Length * 2;
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataSize);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);          // PCM
        writer.Write((short)1);          // mono
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);    // byte rate
        writer.Write((short)2);          // block align
        writer.Write((short)16);         // bits per sample
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataSize);

        foreach (var s in samples)
        {
            var clamped = Math.Clamp(s, -1f, 1f);
            writer.Write((short)(clamped * 32767f));
        }
    }

    private sealed record WavInfo(int SampleRate, int Channels, int BitsPerSample, int FormatTag, long DataSize);
}
