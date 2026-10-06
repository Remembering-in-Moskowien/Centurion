using Centurion.Abstractions.Strategy;
using Centurion.Models;
using Centurion.Core.Workflow.Pipeline.Operators;
using Centurion.Core.Workflow.Strategy.Diarization;
using Centurion.Core.Utils.Parsing;
using Xunit;
namespace Centurion.Tests.Core;

public sealed class DiarizationTests
{
    // ---------- PolyVoiceDiarizationStrategy.BuildArguments ----------

    [Fact]
    public void PolyVoice_BuildArguments_IncludesClustererFormatOutputAndModelsCache()
    {
        var args = PolyVoiceDiarizationStrategy.BuildArguments(
            "audio.wav", "C:\\models\\polyvoice", "out.json", numSpeakers: 0);

        Assert.Contains("diarize", args);
        Assert.Contains("audio.wav", args);
        Assert.Contains("--clusterer", args);
        Assert.Contains("ahc", args);
        Assert.Contains("--format", args);
        Assert.Contains("json", args);
        Assert.Contains("--output", args);
        Assert.Contains("out.json", args);
        Assert.Contains("--models-cache", args);
        Assert.Contains("C:\\models\\polyvoice", args);
        Assert.DoesNotContain("--speakers", args);
    }

    [Fact]
    public void PolyVoice_BuildArguments_NumSpeakersCapsClustering()
    {
        var args = PolyVoiceDiarizationStrategy.BuildArguments(
            "audio.wav", "models", "out.json", numSpeakers: 3);

        Assert.Contains("--speakers", args);
        Assert.Contains("3", args);
    }

    [Fact]
    public void PolyVoice_ParseJson_MapsSegmentsToSpeakerSegments()
    {
        const string json = """
        {
          "segments": [
            { "time": { "start": 0.0, "end": 2.48 }, "speaker": 0, "confidence": 0.98 },
            { "time": { "start": 3.23, "end": 5.45 }, "speaker": 1, "confidence": 0.97 }
          ],
          "turns": []
        }
        """;

        var turns = PolyVoiceDiarizationStrategy.ParseJson(json);

        Assert.Equal(2, turns.Count);
        Assert.Equal(new SpeakerSegment(0.0, 2.48, "SPEAKER_00"), turns[0]);
        Assert.Equal(new SpeakerSegment(3.23, 5.45, "SPEAKER_01"), turns[1]);
    }

    [Fact]
    public void PolyVoice_ParseJson_EmptySegments_ReturnsEmpty()
    {
        Assert.Empty(PolyVoiceDiarizationStrategy.ParseJson("{\"segments\":[]}"));
    }

    // ---------- WeSpeakerDiarizationStrategy.BuildArguments ----------

    [Fact]
    public void WeSpeaker_BuildArguments_IncludesSegmentationEmbeddingAndClusters()
    {
        var args = WeSpeakerDiarizationStrategy.BuildArguments(
            "audio.wav", "seg\\model.onnx", "emb.onnx", numSpeakers: 4);

        Assert.Contains(args, a => a.StartsWith("--segmentation.pyannote-model=", StringComparison.Ordinal));
        Assert.Contains(args, a => a.Contains("seg\\model.onnx", StringComparison.Ordinal));
        Assert.Contains(args, a => a.StartsWith("--embedding.model=", StringComparison.Ordinal));
        Assert.Contains(args, a => a.Contains("emb.onnx", StringComparison.Ordinal));
        Assert.Contains(args, a => a.StartsWith("--clustering.num-clusters=4", StringComparison.Ordinal));
        Assert.Equal("audio.wav", args[^1]);
    }

    [Fact]
    public void WeSpeaker_BuildArguments_NoClusterLimitWhenUnknown()
    {
        var args = WeSpeakerDiarizationStrategy.BuildArguments("a.wav", "seg.onnx", "emb.onnx", 0);

        Assert.DoesNotContain(args, a => a.StartsWith("--clustering.num-clusters", StringComparison.Ordinal));
    }

    [Fact]
    public void WeSpeaker_ParseStdout_MapsTurnLines()
    {
        const string stdout = """
        Started 0.031 -- 6.798 speaker_01
        7.017 -- 13.649 speaker_00
        13.801 -- 16.957 speaker_02
        Duration : 56.861 s
        """;

        var turns = WeSpeakerDiarizationStrategy.ParseStdout(stdout);

        Assert.Equal(3, turns.Count);
        Assert.Equal(new SpeakerSegment(0.031, 6.798, "SPEAKER_01"), turns[0]);
        Assert.Equal(new SpeakerSegment(7.017, 13.649, "SPEAKER_00"), turns[1]);
        Assert.Equal(new SpeakerSegment(13.801, 16.957, "SPEAKER_02"), turns[2]);
    }

    [Fact]
    public void WeSpeaker_ParseStdout_NoTurnLines_ReturnsEmpty()
    {
        Assert.Empty(WeSpeakerDiarizationStrategy.ParseStdout("no turns here\n"));
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

        // The word midpoint falls within interval A (0–5000ms) → speaker 0.
        var wordA = new Word { Text = "hi", Start = 1000, End = 2000, Speaker = "SPEAKER_00" };
        Assert.Equal("speaker 0", DiarizationOperator.ResolveSpeaker(wordA, turns));

        // The word midpoint falls within interval B (5500–10000ms) → speaker 1.
        var wordB = new Word { Text = "yo", Start = 6000, End = 7000, Speaker = "SPEAKER_00" };
        Assert.Equal("speaker 1", DiarizationOperator.ResolveSpeaker(wordB, turns));
    }

    [Fact]
    public void ResolveSpeaker_UnmatchedOrEmpty_FallsBackToNearestOrDefault()
    {
        var turns = new List<SpeakerSegment> { new(0.0, 5.0, "speaker 0") };

        // Falls outside all intervals → fall back to the nearest speaker segment in time (to avoid losing speaker information).
        var wordMiss = new Word { Text = "miss", Start = 8000, End = 9000, Speaker = "SPEAKER_00" };
        Assert.Equal("speaker 0", DiarizationOperator.ResolveSpeaker(wordMiss, turns));

        // Empty turns → default label.
        var wordEmpty = new Word { Text = "x", Start = 0, End = 100, Speaker = "SPEAKER_00" };
        Assert.Equal("SPEAKER_00", DiarizationOperator.ResolveSpeaker(wordEmpty, []));
    }

    // ---------- ResolveSpeaker: maximizing overlap with the time window ----------

    [Fact]
    public void ResolveSpeaker_BoundaryWord_AssignsToLargerOverlap()
    {
        // The word (4500–6500ms) spans both A (0–5000ms) and B (5500–10000ms):
        // it overlaps A by 500ms and B by 1000ms → assigned to B (the side with the larger overlap).
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
        // A(0–3) B(3–3.4) A(3.4–6): B is a 0.4s fragment flanked by A on both sides → absorbed into a single A segment.
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
        // B(3–3.4) is shorter than the threshold, with different speakers on both sides → merged into the longer neighbor A (0–3, 3s > C 2.6s).
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
        // Out-of-order and overlapping input: sort by start time and trim overlaps.
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
