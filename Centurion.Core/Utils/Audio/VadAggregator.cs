using System.Buffers.Binary;
using System.Text;
using Centurion.Models.Workflow;

namespace Centurion.Core.Utils.Audio;

/// <summary>
/// 语音段聚合器：把 VAD 检出的语音段从源音频抽取并拼接为一段连续音频，
/// 段间插入小段静音缓冲（防止分离/转写时段边界互渗），并回填每段的聚合起始位置。
/// 聚合产物供人声分离（Demucs）使用，避免器乐/静音段干扰分离质量；
/// 转录结果再按聚合位置映射还原到源时间轴。
/// </summary>
public static class VadAggregator
{
    /// <summary>
    /// 聚合语音段并写出标准 PCM s16 单声道 WAV。
    /// </summary>
    /// <param name="samples">源音频单声道样本（-1..1）。</param>
    /// <param name="sampleRate">源采样率（Hz）。</param>
    /// <param name="segments">VAD 检出的语音段（按 StartMs 升序）。</param>
    /// <param name="outputPath">聚合 WAV 输出路径。</param>
    /// <param name="padSeconds">段间静音缓冲时长（秒），默认 0.1。</param>
    /// <returns>回填了 <see cref="VoiceSegment.AggStartMs"/> 的段列表（与原列表同序）。</returns>
    public static IReadOnlyList<VoiceSegment> Aggregate(
        float[] samples,
        int sampleRate,
        IReadOnlyList<VoiceSegment> segments,
        string outputPath,
        double padSeconds = 0.1)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(segments);
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("Output path is required.", nameof(outputPath));

        var padSamples = Math.Max(0, (int)Math.Round(padSeconds * sampleRate));
        var ordered = segments.OrderBy(s => s.StartMs).ToList();

        var outSamples = new List<int>();
        foreach (var seg in ordered)
        {
            var start = (int)Math.Round(seg.StartMs / 1000.0 * sampleRate);
            var end = (int)Math.Round(seg.EndMs / 1000.0 * sampleRate);
            start = Math.Clamp(start, 0, samples.Length);
            end = Math.Clamp(end, start, samples.Length);

            if (outSamples.Count > 0)
                outSamples.AddRange(Enumerable.Repeat(0, padSamples));

            var aggStart = outSamples.Count;
            seg.AggStartMs = aggStart / (double)sampleRate * 1000.0;

            for (var i = start; i < end; i++)
                outSamples.Add(SampleToS16(samples[i]));
        }

        WritePcmS16Wav(outSamples, sampleRate, outputPath);
        return ordered;
    }

    private static int SampleToS16(float v)
    {
        var x = Math.Clamp(v, -1f, 1f);
        return (int)Math.Round(x * 32767);
    }

    /// <summary>写出标准 RIFF PCM s16 单声道 WAV（无扩展块，广泛兼容）。</summary>
    internal static void WritePcmS16Wav(IReadOnlyList<int> samples, int sampleRate, string path)
    {
        var dataSize = samples.Count * 2;
        using var stream = new MemoryStream();
        using (var bw = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
        {
            bw.Write(Encoding.ASCII.GetBytes("RIFF"));
            bw.Write(36 + dataSize);
            bw.Write(Encoding.ASCII.GetBytes("WAVE"));
            bw.Write(Encoding.ASCII.GetBytes("fmt "));
            bw.Write(16);
            bw.Write((short)1);          // PCM
            bw.Write((short)1);          // mono
            bw.Write(sampleRate);
            bw.Write(sampleRate * 2);    // byte rate
            bw.Write((short)2);          // block align
            bw.Write((short)16);         // bits
            bw.Write(Encoding.ASCII.GetBytes("data"));
            bw.Write(dataSize);
            foreach (var s in samples)
                bw.Write((short)s);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllBytes(path, stream.ToArray());
    }
}
