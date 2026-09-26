using Centurion.Models;
using Centurion.Models.Workflow;
using Centurion.Core.Utils.Audio;
using Centurion.Core.Workflow.Pipeline.Operators;
using Xunit;

namespace Centurion.Tests.Core;

/// <summary>
/// VAD 检测/聚合/时间轴还原测试：器乐与静音段剔除、语音段聚合、聚合轴还原回源轴。
/// </summary>
public class VadTests
{
    private const int Rate = 16000;

    private static float[] Sine(int seconds, double amplitude)
    {
        var samples = new float[seconds * Rate];
        for (var i = 0; i < samples.Length; i++)
            samples[i] = (float)(amplitude * Math.Sin(2 * Math.PI * 220 * i / Rate));
        return samples;
    }

    private static float[] Concat(params float[][] parts)
    {
        var total = parts.Sum(p => p.Length);
        var result = new float[total];
        var offset = 0;
        foreach (var p in parts)
        {
            Array.Copy(p, 0, result, offset, p.Length);
            offset += p.Length;
        }
        return result;
    }

    private static VadDetector.VadOptions Options(double merge = 1.0, double minSpeech = 0.3, double ratio = 0.2)
        => new(WindowSeconds: 0.5, MergeGapSeconds: merge, MinSpeechSeconds: minSpeech,
               EnergyThresholdRatio: ratio, AbsoluteFloorDb: -50);

    [Fact]
    public void Detect_FindsSingleSpeechSegmentBetweenSilence()
    {
        // 0.5s 静音 + 1.0s 正弦 + 0.5s 静音 → 1 段 [500,1500)ms
        var samples = Concat(new float[Rate / 2], Sine(1, 0.3f), new float[Rate / 2]);
        var segments = VadDetector.Detect(samples, Rate, Options());

        Assert.Single(segments);
        Assert.Equal(500, segments[0].StartMs, 0);
        Assert.Equal(1500, segments[0].EndMs, 0);
    }

    [Fact]
    public void Detect_SplitsSegmentsWhenGapExceedsMergeWindow()
    {
        // 0.5s 语音 + 2.0s 静音 + 0.5s 语音（gap > mergeGap 1s）→ 2 段
        var samples = Concat(Sine(1, 0.3f), new float[Rate * 2], Sine(1, 0.3f));
        var segments = VadDetector.Detect(samples, Rate, Options());

        Assert.Equal(2, segments.Count);
    }

    [Fact]
    public void Detect_MergesSegmentsWhenGapIsSmall()
    {
        // 0.5s 语音 + 0.5s 静音 + 0.5s 语音（gap 0.5s < mergeGap 1s）→ 1 段
        var samples = Concat(Sine(1, 0.3f), new float[Rate / 2], Sine(1, 0.3f));
        var segments = VadDetector.Detect(samples, Rate, Options());

        Assert.Single(segments);
    }

    [Fact]
    public void Detect_DropsSegmentsShorterThanMinSpeech()
    {
        // 0.7s 语音跨 2 个 0.5s 窗口（跨度 1.0s）；minSpeech=1.5 → 丢弃
        var samples = Concat(new float[Rate / 2], Sine(1, 0.3f)[..(int)(0.7 * Rate)], new float[Rate / 2]);
        var segments = VadDetector.Detect(samples, Rate, Options(minSpeech: 1.5));

        Assert.Empty(segments);
    }

    [Fact]
    public void Aggregate_WritesWavAndBackfillsAggStart()
    {
        var segments = new List<VoiceSegment>
        {
            new() { StartMs = 0, EndMs = 1000 },
            new() { StartMs = 5000, EndMs = 6000 }
        };
        var samples = Concat(Sine(2, 0.3f), new float[Rate * 4], Sine(2, 0.3f));
        var outPath = Path.Combine(Path.GetTempPath(), $"vadtest_{Guid.NewGuid():N}.wav");
        try
        {
            var mapped = VadAggregator.Aggregate(samples, Rate, segments, outPath, padSeconds: 0.1);
            Assert.True(File.Exists(outPath));
            Assert.Equal(0, mapped[0].AggStartMs, 0);
            // 段 2 前：段 1 (1000ms) + 缓冲 (100ms) = 1100ms
            Assert.Equal(1100, mapped[1].AggStartMs, 1);
            Assert.Equal(2, mapped.Count);
        }
        finally
        {
            if (File.Exists(outPath))
                File.Delete(outPath);
        }
    }

    [Fact]
    public void MapWordsToSource_RestoresSourceTimeline()
    {
        var map = new List<VoiceSegment>
        {
            new() { StartMs = 0, EndMs = 10000, AggStartMs = 0 },
            new() { StartMs = 45000, EndMs = 60000, AggStartMs = 11000 }
        };
        var words = new List<Word>
        {
            new() { Text = "a", Start = 2000, End = 3000, Speaker = "SPEAKER_00" },     // 段 1 → 源 2000-3000
            new() { Text = "b", Start = 12000, End = 13000, Speaker = "SPEAKER_00" }    // 段 2 → 源 46000-47000
        };

        var mapped = TranscribeOperator.MapWordsToSource(words, map);

        Assert.Equal(2000, mapped[0].Start, 0);
        Assert.Equal(3000, mapped[0].End, 0);
        Assert.Equal(46000, mapped[1].Start, 0);
        Assert.Equal(47000, mapped[1].End, 0);
    }

    [Fact]
    public void MapWordsToSource_NoMap_ReturnsWordsAsIs()
    {
        var words = new List<Word> { new() { Text = "x", Start = 1, End = 2, Speaker = "SPEAKER_00" } };
        var mapped = TranscribeOperator.MapWordsToSource(words, new List<VoiceSegment>());
        Assert.Single(mapped);
        Assert.Equal("x", mapped[0].Text);
    }
}
