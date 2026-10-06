using Centurion.Core.Workflow.Pipeline.Operators;using Centurion.Models;
using Centurion.Models.Workflow;
using Xunit;
using Centurion.Core.Utils.Reporting;
namespace Centurion.Tests.Core;

/// <summary>
/// Unit tests for dub Phase 2/3: SNR candidate scoring, long-sentence chunking, overlap degradation, and Dub metrics in the quality report.
/// </summary>
public class DubPhase23Tests
{
    // ── SpeakerProfilingOperator: SNR scoring and RMS parsing ──

    [Fact]
    public void ScoreCandidate_PrefersHighSnrAndTargetDuration()
    {
        var candidate = new Sentence { Start = 1000, End = 5000 }; // 4s = target duration

        var highSnr = SpeakerProfilingOperator.ScoreCandidate(candidate, -40, -10); // SNR 30dB
        var lowSnr = SpeakerProfilingOperator.ScoreCandidate(candidate, -40, -35);  // SNR 5dB

        Assert.True(highSnr > lowSnr);
    }

    [Fact]
    public void ScoreCandidate_FavorsDurationCloseToTarget()
    {
        var perfect = new Sentence { Start = 0, End = 4000 };    // 4s
        var tooShort = new Sentence { Start = 0, End = 2000 };   // 2s

        var noise = -40.0;
        Assert.True(SpeakerProfilingOperator.ScoreCandidate(perfect, noise, -10) >
                    SpeakerProfilingOperator.ScoreCandidate(tooShort, noise, -10));
    }

    [Fact]
    public void ParseRmsDb_ExtractsValueFromAstatsMetadata()
    {
        const string output = "frame:0    pts:0       pts_time:0\nlavfi.astats.Overall.RMS_level=-23.5dB\nlavfi.astats.Overall.RMS_peak=-10.0dB";

        var db = SpeakerProfilingOperator.ParseRmsDb(output);

        Assert.Equal(-23.5, db);
    }

    [Fact]
    public void ParseRmsDb_ReturnsNull_WhenNoMatch()
    {
        Assert.Null(SpeakerProfilingOperator.ParseRmsDb("nothing useful here"));
    }

    // ── TtsSynthesisOperator: long-sentence chunking ──

    [Fact]
    public void SplitLongSentence_NoChunk_WhenWithinThreshold()
    {
        var parts = TtsSynthesisOperator.SplitLongSentence("Hello world.", 3000, 15);
        Assert.Single(parts);
        Assert.Equal("Hello world.", parts[0]);
    }

    [Fact]
    public void SplitLongSentence_ChunksWhenExceedingThreshold()
    {
        var longText = string.Join(' ', Enumerable.Repeat("word", 80)); // long sentence of 80 words
        var parts = TtsSynthesisOperator.SplitLongSentence(longText, 40000, 15); // 40s target

        Assert.True(parts.Count >= 2, $"expected >=2 chunks, got {parts.Count}");
        Assert.True(parts.Count <= 8);
        Assert.Equal(longText.Replace(" ", ""), string.Concat(parts).Replace(" ", ""));
    }

    [Fact]
    public void SplitText_CjkSplitsByCharacters()
    {
        var parts = TtsSynthesisOperator.SplitText("这是一个很长的中文句子用来测试分块逻辑是否正确工作", 3);

        Assert.Equal(3, parts.Count);
        Assert.True(parts.All(p => p.Length > 0));
        Assert.Equal("这是一个很长的中文句子用来测试分块逻辑是否正确工作",
            string.Concat(parts));
    }

    // ── TimeAlignmentOperator: overlap degradation ──

    [Fact]
    public void DetectOverlaps_SetsMixOffset_AndNotes()
    {
        var segments = new List<DubSegment>
        {
            new() { Text = "A", TargetStartMs = 0, TargetEndMs = 3000 },
            new() { Text = "B", TargetStartMs = 2500, TargetEndMs = 5000 } // overlaps A by 500ms
        };

        TimeAlignmentOperator.DetectOverlaps(segments);

        Assert.Equal(500, segments[1].MixOffsetMs);
        Assert.Contains("Overlap", segments[1].Note!);
    }

    [Fact]
    public void DetectOverlaps_NoOffset_WhenNonOverlapping()
    {
        var segments = new List<DubSegment>
        {
            new() { Text = "A", TargetStartMs = 0, TargetEndMs = 3000 },
            new() { Text = "B", TargetStartMs = 3100, TargetEndMs = 5000 }
        };

        TimeAlignmentOperator.DetectOverlaps(segments);

        Assert.Equal(0, segments[1].MixOffsetMs);
    }

    // ── TimeAlignmentOperator: stretch engine (rubberband / atempo) ──

    [Fact]
    public void BuildStretchFilter_Atempo_ReturnsAtempoFilter()
    {
        Assert.Equal("atempo=0.5", TimeAlignmentOperator.BuildStretchFilter(0.5, useRubberband: false));
    }

    [Fact]
    public void BuildStretchFilter_Rubberband_ReturnsFormantPreservingFilter()
    {
        Assert.Equal(
            "rubberband=tempo=0.5:formant=preserved",
            TimeAlignmentOperator.BuildStretchFilter(0.5, useRubberband: true));
    }

    [Fact]
    public void BuildStretchFilter_TempoUsesInvariantCulture()
    {
        // 3 s synthesized speech into a 6 s target = tempo 0.5; must never render as "0,5" on a comma-locale.
        Assert.Equal("atempo=0.5", TimeAlignmentOperator.BuildStretchFilter(3.0 / 6.0, useRubberband: false));
    }

    // ── QualityReportBuilder: Dub metrics ──

    [Fact]
    public void BuildDub_ComputesCoverageAndTempos()
    {
        var config = new WorkflowConfig { CommandName = "dub", TtsLanguage = "en" };
        var state = new WorkflowState();
        var sentences = new List<Sentence>
        {
            new() { Text = "Hello.", TranslatedText = "你好。", Start = 0, End = 1000 },
            new() { Text = "World.", Start = 1000, End = 2000 } // no translation
        };
        state.CurrentSentences.AddRange(sentences);
        state.DubSegments = new List<DubSegment>
        {
            new() { Text = "你好。", TargetStartMs = 0, TargetEndMs = 1000, AlignmentTempo = 1.1, AlignedDurationSec = 1.0, SynthesizedDurationSec = 1.2 },
            new() { Text = "World.", TargetStartMs = 1000, TargetEndMs = 2000, AlignmentTempo = 0.9, AlignedDurationSec = 1.0, SynthesizedDurationSec = 1.3 }
        };
        var context = new SubtitleWorkflowContext(config);
        context.State = state;

        var report = QualityReportBuilder.Build(context, "out.wav", 10);

        Assert.NotNull(report.Dub);
        Assert.Equal(2, report.Dub!.SegmentsTotal);
        Assert.Equal(0, report.Dub.SegmentsSkipped);
        Assert.Equal(1.0, report.Dub.TranslationCoverage); // both sentences have dubbing text (translation or source)
        Assert.Equal(0.9, report.Dub.TempoMin);
        Assert.Equal(1.1, report.Dub.TempoMax);
    }

    [Fact]
    public void BuildDub_ReturnsNull_ForNonDubCommands()
    {
        var config = new WorkflowConfig { CommandName = "spawn" };
        var context = new SubtitleWorkflowContext(config);

        var report = QualityReportBuilder.Build(context, "out.ass", 10);

        Assert.Null(report.Dub);
    }
}
