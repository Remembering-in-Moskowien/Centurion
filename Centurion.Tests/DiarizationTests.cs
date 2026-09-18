using Centurion.Core.Abstractions.Strategy;
using Centurion.Core.Models;
using Centurion.Core.Pipeline.Operators;
using Centurion.Core.Strategy.Diarization;
using Centurion.Core.Utils;
using Xunit;

namespace Centurion.Tests;

public sealed class DiarizationTests
{
    // ---------- DiarizationJsonParser ----------

    [Fact]
    public void DiarizationJsonParser_ParsesSegmentsAndSpeakers()
    {
        const string json = """
        {
          "task": "transcribe",
          "language": "en",
          "duration": 30.0,
          "segments": [
            { "id": 0, "start": 0.0, "end": 5.0, "speaker": "A", "type": "transcript.text.segment" },
            { "id": 1, "start": 5.5, "end": 10.2, "speaker": "B" },
            { "id": 2, "start": 12.0, "end": 15.0 }
          ]
        }
        """;

        var turns = DiarizationJsonParser.Parse(json);

        Assert.Equal(2, turns.Count);
        Assert.Equal(new SpeakerSegment(0.0, 5.0, "A"), turns[0]);
        Assert.Equal(new SpeakerSegment(5.5, 10.2, "B"), turns[1]);
    }

    [Fact]
    public void DiarizationJsonParser_MissingSegments_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => DiarizationJsonParser.Parse("{\"text\":\"no segments\"}"));
    }

    // ---------- DiarizationOperator.ResolveSpeaker ----------

    [Fact]
    public void ResolveSpeaker_MatchesByWordMidpoint()
    {
        var turns = new List<SpeakerSegment>
        {
            new(0.0, 5.0, "A"),
            new(5.5, 10.0, "B")
        };

        // 时间中点落在 A 区间（0–5000ms）→ A
        var wordA = new Word { Text = "hi", Start = 1000, End = 2000, Speaker = "SPEAKER_00" };
        Assert.Equal("A", DiarizationOperator.ResolveSpeaker(wordA, turns));

        // 时间中点落在 B 区间（5500–10000ms）→ B
        var wordB = new Word { Text = "yo", Start = 6000, End = 7000, Speaker = "SPEAKER_00" };
        Assert.Equal("B", DiarizationOperator.ResolveSpeaker(wordB, turns));
    }

    [Fact]
    public void ResolveSpeaker_UnmatchedOrEmpty_FallsBackToDefault()
    {
        var turns = new List<SpeakerSegment> { new(0.0, 5.0, "A") };

        // 落在区间外 → 默认标签
        var wordMiss = new Word { Text = "miss", Start = 8000, End = 9000, Speaker = "SPEAKER_00" };
        Assert.Equal("SPEAKER_00", DiarizationOperator.ResolveSpeaker(wordMiss, turns));

        // 空 turns → 默认标签
        var wordEmpty = new Word { Text = "x", Start = 0, End = 100, Speaker = "SPEAKER_00" };
        Assert.Equal("SPEAKER_00", DiarizationOperator.ResolveSpeaker(wordEmpty, []));
    }

    // ---------- CrispAsrDiarizationBase.BuildArguments ----------

    [Fact]
    public void BuildArguments_CrispAsrMethod_NoEmbedderOrSegmentModel()
    {
        var args = CrispAsrDiarizationBase.BuildArguments(
            "audio.wav", "C:\\models\\ggml-tiny.bin", "audio_diar",
            numSpeakers: 0, segmentModel: null, method: "foxnose", embedder: null, defaultSegmentModel: null);

        Assert.Contains("--diarize --diarize-method foxnose", args);
        Assert.DoesNotContain("--diarize-embedder", args);
        Assert.DoesNotContain("--sherpa-segment-model", args);
        Assert.DoesNotContain("--diarize-max-speakers", args);
        Assert.Contains("-ojf -of \"audio_diar\"", args);
    }

    [Fact]
    public void BuildArguments_PyannoteTitaNet_IncludesEmbedderSegmentModelAndMaxSpeakers()
    {
        var args = CrispAsrDiarizationBase.BuildArguments(
            "audio.wav", "C:\\models\\ggml-tiny.bin", "audio_diar",
            numSpeakers: 3, segmentModel: "pyannote-seg-3.0",
            method: "pyannote", embedder: "auto", defaultSegmentModel: null);

        Assert.Contains("--diarize-method pyannote", args);
        Assert.Contains("--diarize-embedder auto", args);
        Assert.Contains("--sherpa-segment-model pyannote-seg-3.0", args);
        Assert.Contains("--diarize-max-speakers 3", args);
    }

    [Fact]
    public void BuildArguments_UsesDefaultSegmentModelWhenConfigValueMissing()
    {
        var args = CrispAsrDiarizationBase.BuildArguments(
            "audio.wav", "C:\\models\\ggml-tiny.bin", "audio_diar",
            numSpeakers: 0, segmentModel: null,
            method: "pyannote", embedder: "auto", defaultSegmentModel: "pyannote-seg-3.0");

        Assert.Contains("--sherpa-segment-model pyannote-seg-3.0", args);
    }
}
