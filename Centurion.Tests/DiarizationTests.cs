using Centurion.Abstractions.Strategy;
using Centurion.Models;
using Centurion.Core.Pipeline.Operators;
using Centurion.Core.Strategy.Diarization;
using Centurion.Core.Utils;
using Xunit;

namespace Centurion.Tests;

public sealed class DiarizationTests
{
    // ---------- DiarizationJsonParser ----------

    [Fact]
    public void DiarizationJsonParser_ParsesSpeakersFromTranscription()
    {
        const string json = """
        {
          "crispasr": { "backend": "whisper", "model": "ggml-tiny.bin", "language": "en" },
          "transcription": [
            {
              "timestamps": { "from": "00:00:00,000", "to": "00:00:05,000" },
              "offsets":    { "from": 0, "to": 5000 },
              "speaker":    "(speaker 0) ",
              "text":       "Hello there",
              "chunk_id":   0
            },
            {
              "timestamps": { "from": "00:00:05,500", "to": "00:00:10,200" },
              "offsets":    { "from": 5500, "to": 10200 },
              "speaker":    "(speaker 1) ",
              "text":       "Hi!",
              "chunk_id":   0
            },
            {
              "timestamps": { "from": "00:00:12,000", "to": "00:00:15,000" },
              "offsets":    { "from": 12000, "to": 15000 },
              "text":       "No speaker label",
              "chunk_id":   1
            }
          ]
        }
        """;

        var turns = DiarizationJsonParser.Parse(json);

        Assert.Equal(2, turns.Count);
        Assert.Equal(new SpeakerSegment(0.0, 5.0, "speaker 0"), turns[0]);
        Assert.Equal(new SpeakerSegment(5.5, 10.2, "speaker 1"), turns[1]);
    }

    [Fact]
    public void DiarizationJsonParser_MissingTranscription_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => DiarizationJsonParser.Parse("{\"text\":\"no transcription\"}"));
    }

    // ---------- DiarizationOperator.ResolveSpeaker ----------

    [Fact]
    public void ResolveSpeaker_MatchesByWordMidpoint()
    {
        var turns = new List<SpeakerSegment>
        {
            new(0.0, 5.0, "speaker 0"),
            new(5.5, 10.0, "speaker 1")
        };

        // 时间中点落在 A 区间（0–5000ms）→ speaker 0
        var wordA = new Word { Text = "hi", Start = 1000, End = 2000, Speaker = "SPEAKER_00" };
        Assert.Equal("speaker 0", DiarizationOperator.ResolveSpeaker(wordA, turns));

        // 时间中点落在 B 区间（5500–10000ms）→ speaker 1
        var wordB = new Word { Text = "yo", Start = 6000, End = 7000, Speaker = "SPEAKER_00" };
        Assert.Equal("speaker 1", DiarizationOperator.ResolveSpeaker(wordB, turns));
    }

    [Fact]
    public void ResolveSpeaker_UnmatchedOrEmpty_FallsBackToNearestOrDefault()
    {
        var turns = new List<SpeakerSegment> { new(0.0, 5.0, "speaker 0") };

        // 落在区间外 → 回退到时间最近的说话人片段（避免说话人信息丢失）
        var wordMiss = new Word { Text = "miss", Start = 8000, End = 9000, Speaker = "SPEAKER_00" };
        Assert.Equal("speaker 0", DiarizationOperator.ResolveSpeaker(wordMiss, turns));

        // 空 turns → 默认标签
        var wordEmpty = new Word { Text = "x", Start = 0, End = 100, Speaker = "SPEAKER_00" };
        Assert.Equal("SPEAKER_00", DiarizationOperator.ResolveSpeaker(wordEmpty, []));
    }

    // ---------- CrispAsrDiarizationBase.BuildArguments ----------

    [Fact]
    public void BuildArguments_CrispAsrMethod_EnablesSpeakersNoEmbedderOrMaxSpeakers()
    {
        var args = CrispAsrDiarizationBase.BuildArguments(
            "audio.wav", "C:\\models\\ggml-tiny.bin", "audio_diar",
            numSpeakers: 0, segmentModel: null, method: "foxnose", embedder: null, defaultSegmentModel: null);

        Assert.Contains("--diarize-speakers --diarize-method foxnose", args);
        Assert.DoesNotContain("--diarize-embedder", args);
        Assert.DoesNotContain("--sherpa-segment-model", args);
        Assert.DoesNotContain("--diarize-max-speakers", args);
        Assert.Contains("-ojf -of \"audio_diar\"", args);
    }

    [Fact]
    public void BuildArguments_PyannoteTitaNet_IncludesEmbedderAndMaxSpeakers()
    {
        var args = CrispAsrDiarizationBase.BuildArguments(
            "audio.wav", "C:\\models\\ggml-tiny.bin", "audio_diar",
            numSpeakers: 3, segmentModel: "pyannote-seg-3.0",
            method: "pyannote", embedder: "auto", defaultSegmentModel: null);

        Assert.Contains("--diarize-speakers --diarize-method pyannote", args);
        Assert.Contains("--diarize-embedder auto", args);
        Assert.Contains("--diarize-max-speakers 3", args);
        Assert.DoesNotContain("--sherpa-segment-model", args);
    }

    [Fact]
    public void BuildArguments_SegmentModelIsManagedByCli_NotPassed()
    {
        var args = CrispAsrDiarizationBase.BuildArguments(
            "audio.wav", "C:\\models\\ggml-tiny.bin", "audio_diar",
            numSpeakers: 0, segmentModel: "pyannote-seg-3.0",
            method: "pyannote", embedder: "auto", defaultSegmentModel: "pyannote-seg-3.0");

        Assert.DoesNotContain("--sherpa-segment-model", args);
        Assert.DoesNotContain("pyannote-seg-3.0", args);
    }

    // ---------- ResolveSpeaker：时间窗重叠最大化 ----------

    [Fact]
    public void ResolveSpeaker_BoundaryWord_AssignsToLargerOverlap()
    {
        // 词 (4500–6500ms) 横跨 A(0–5000ms) 与 B(5500–10000ms)：
        // 与 A 重叠 500ms，与 B 重叠 1000ms → 归 B（重叠更大的一侧）
        var turns = new List<SpeakerSegment>
        {
            new(0.0, 5.0, "speaker 0"),
            new(5.5, 10.0, "speaker 1")
        };

        var boundary = new Word { Text = "both", Start = 4500, End = 6500, Speaker = "SPEAKER_00" };
        Assert.Equal("speaker 1", DiarizationOperator.ResolveSpeaker(boundary, turns));
    }

    [Fact]
    public void ResolveSpeaker_FullyInside_AssignsContainingTurn()
    {
        var turns = new List<SpeakerSegment> { new(2.0, 8.0, "speaker 0") };

        var inside = new Word { Text = "in", Start = 3000, End = 4000, Speaker = "SPEAKER_00" };
        Assert.Equal("speaker 0", DiarizationOperator.ResolveSpeaker(inside, turns));
    }

    // ---------- SpeakerSegmentSmoother ----------

    [Fact]
    public void Smooth_MergesAdjacentSameSpeaker()
    {
        var turns = new List<SpeakerSegment>
        {
            new(0.0, 2.0, "speaker 0"),
            new(2.0, 4.0, "speaker 0"),
            new(4.0, 6.0, "speaker 1")
        };

        var smoothed = SpeakerSegmentSmoother.Smooth(turns);

        Assert.Equal(2, smoothed.Count);
        Assert.Equal(new SpeakerSegment(0.0, 4.0, "speaker 0"), smoothed[0]);
        Assert.Equal(new SpeakerSegment(4.0, 6.0, "speaker 1"), smoothed[1]);
    }

    [Fact]
    public void Smooth_RemovesAlternatingBlip_BetweenSameSpeakers()
    {
        // A(0–3) B(3–3.4) A(3.4–6)：B 为 0.4s 碎片且前后同为 A → 吞并为一个 A 段
        var turns = new List<SpeakerSegment>
        {
            new(0.0, 3.0, "speaker 0"),
            new(3.0, 3.4, "speaker 1"),
            new(3.4, 6.0, "speaker 0")
        };

        var smoothed = SpeakerSegmentSmoother.Smooth(turns, minDurationSeconds: 0.5);

        Assert.Single(smoothed);
        Assert.Equal(new SpeakerSegment(0.0, 6.0, "speaker 0"), smoothed[0]);
    }

    [Fact]
    public void Smooth_ShortFragment_MergesIntoLongerNeighbor()
    {
        // B(3–3.4) 短于阈值，前后为不同说话人 → 并入相邻较长的 A(0–3, 3s > C 2.6s)
        var turns = new List<SpeakerSegment>
        {
            new(0.0, 3.0, "speaker 0"),
            new(3.0, 3.4, "speaker 1"),
            new(3.4, 6.0, "speaker 2")
        };

        var smoothed = SpeakerSegmentSmoother.Smooth(turns, minDurationSeconds: 0.5);

        Assert.Equal(2, smoothed.Count);
        Assert.Equal(new SpeakerSegment(0.0, 3.4, "speaker 0"), smoothed[0]);
        Assert.Equal(new SpeakerSegment(3.4, 6.0, "speaker 2"), smoothed[1]);
    }

    [Fact]
    public void Smooth_SortsAndRemovesOverlap()
    {
        // 乱序 + 交叠输入：按开始时间排序并裁剪重叠
        var turns = new List<SpeakerSegment>
        {
            new(5.0, 8.0, "speaker 1"),
            new(0.0, 5.0, "speaker 0"),
            new(4.0, 6.0, "speaker 2")
        };

        var smoothed = SpeakerSegmentSmoother.Smooth(turns);

        Assert.Equal(3, smoothed.Count);
        Assert.Equal("speaker 0", smoothed[0].Speaker);
        Assert.Equal("speaker 2", smoothed[1].Speaker);
        Assert.Equal(5.0, smoothed[1].StartSeconds);
        Assert.Equal("speaker 1", smoothed[2].Speaker);
        for (var i = 1; i < smoothed.Count; i++)
            Assert.True(smoothed[i].StartSeconds >= smoothed[i - 1].EndSeconds);
    }

    [Fact]
    public void Smooth_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(SpeakerSegmentSmoother.Smooth([]));
    }
}
