namespace Centurion.Core.Utils.Media;

/// <summary>
/// Rough signal-to-noise ratio (SNR) estimator (dB) based on the RMS of WAV sample frames.
/// </summary>
public static class WavSnrEstimator
{
    /// <summary>
    /// Estimate the signal-to-noise ratio (dB) of the given 16-bit PCM WAV file: computes RMS per frame,
    /// using the mean square of low-energy frames as noise and the mean square of high-energy segments as the signal.
    /// </summary>
    /// <param name="path">Path to the WAV file.</param>
    /// <returns>The estimated signal-to-noise ratio (dB); returns 40 when there are too few data frames.</returns>
    /// <exception cref="FormatException">Thrown when the file is not a supported PCM WAV format.</exception>
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
