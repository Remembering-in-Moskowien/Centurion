using System.Text;

namespace Centurion.Core.Utils.Audio;

/// <summary>
/// 轻量 WAV 读取器：把 PCM WAV 解码为单声道 float 样本（-1..1）。
/// 支持 PCM s16/s32 与 IEEE float（含 Demucs 输出的 WAVE_FORMAT_EXTENSIBLE 容器）。
/// 供 VAD 检测与语音段聚合使用，避免为纯算法引入额外媒体依赖。
/// </summary>
public static class WavSampleReader
{
    /// <summary>解析结果：单声道样本 + 采样率。</summary>
    /// <param name="Samples">单声道 float 样本序列。</param>
    /// <param name="SampleRate">采样率（Hz）。</param>
    public readonly record struct ReadResult(float[] Samples, int SampleRate);

    /// <summary>
    /// 读取 WAV 文件并输出单声道样本（多声道取各声道平均）。
    /// </summary>
    /// <param name="path">WAV 文件路径。</param>
    /// <returns>样本与采样率。</returns>
    /// <exception cref="InvalidDataException">文件不是受支持的 PCM WAV。</exception>
    public static ReadResult ReadMono(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 44 || Encoding.ASCII.GetString(bytes, 0, 4) != "RIFF"
            || Encoding.ASCII.GetString(bytes, 8, 4) != "WAVE")
            throw new InvalidDataException($"Not a RIFF WAVE file: {path}");

        var pos = 12;
        int fmtCode = 0, channels = 0, sampleRate = 0, bits = 0;
        int dataOffset = -1, dataSize = 0;

        while (pos + 8 <= bytes.Length)
        {
            var id = Encoding.ASCII.GetString(bytes, pos, 4);
            var size = BitConverter.ToInt32(bytes, pos + 4);
            if (id == "fmt ")
            {
                fmtCode = BitConverter.ToUInt16(bytes, pos + 8);
                channels = BitConverter.ToUInt16(bytes, pos + 10);
                sampleRate = BitConverter.ToInt32(bytes, pos + 12);
                bits = BitConverter.ToUInt16(bytes, pos + 22);
                // WAVE_FORMAT_EXTENSIBLE：SubFormat GUID 标识实际编码
                if (fmtCode == 0xFFFE && size >= 40)
                {
                    var sub = bytes.AsSpan(pos + 32, 16);
                    fmtCode = sub[0] == 0x01 ? 1 : sub[0] == 0x03 ? 3 : fmtCode;
                }
            }
            else if (id == "data")
            {
                dataOffset = pos + 8;
                dataSize = size;
                break;
            }
            pos += 8 + size + (size & 1);
        }

        if (dataOffset < 0 || channels <= 0 || sampleRate <= 0)
            throw new InvalidDataException($"WAV has no usable data chunk: {path}");

        var payload = bytes.AsSpan(dataOffset, Math.Min(dataSize, bytes.Length - dataOffset));
        var frameBytes = bits / 8;
        if (frameBytes <= 0)
            throw new InvalidDataException($"Unsupported bits per sample: {bits}");

        var frameCount = payload.Length / (frameBytes * channels);
        var samples = new float[frameCount];

        for (var i = 0; i < frameCount; i++)
        {
            double sum = 0;
            for (var c = 0; c < channels; c++)
            {
                var off = (i * channels + c) * frameBytes;
                sum += DecodeSample(payload, off, fmtCode, bits);
            }
            samples[i] = (float)(sum / channels);
        }

        return new ReadResult(samples, sampleRate);
    }

    private static double DecodeSample(ReadOnlySpan<byte> data, int offset, int fmtCode, int bits)
    {
        if (fmtCode == 3) // IEEE float（含 EXTENSIBLE float）
        {
            var raw = BitConverter.ToSingle(data.Slice(offset, 4));
            return double.IsFinite(raw) ? raw : 0;
        }

        // PCM：s16/s32，按位深规范化到 -1..1
        if (bits == 16)
            return BitConverter.ToInt16(data.Slice(offset, 2)) / 32768.0;
        if (bits == 32)
            return BitConverter.ToInt32(data.Slice(offset, 4)) / 2147483648.0;
        if (bits == 8)
            return (data[offset] - 128) / 128.0;
        throw new InvalidDataException($"Unsupported PCM bit depth: {bits}");
    }
}
