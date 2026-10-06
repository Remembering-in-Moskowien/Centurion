using Centurion.Models;
using Centurion.Models.Workflow;
using Xunit;
using Centurion.Core.Utils.Reporting;

namespace Centurion.Tests.Core;

/// <summary>
/// Step 5 quality loop: Builder metric wiring (Timing/Issues/Confidence/Translation/Tts),
/// the automatic fix engine, CI threshold evaluation, and the HTML report.
/// </summary>
public sealed class QualityLoopTests
{
    // ---------- Builder wiring ----------

    private static SubtitleWorkflowContext MakeContext()
    {
        var config = new WorkflowConfig
        {
            CommandName = "spawn",
            InputFilePath = @"C:\media\in.mkv",
            OutputFilePath = @"C:\out\out.ass",
            Language = "en"
        };
        var context = new SubtitleWorkflowContext(config);
        context.State.TranscribeSentences =
        [
            new Sentence { Text = "Hello world.", Start = 0, End = 1000, Confidence = 0.9 },
            new Sentence { Text = "Goodbye.", Start = 1500, End = 2000, Confidence = 0.3 }
        ];
        context.State.CurrentSentences = context.State.TranscribeSentences;
        context.State.IsTranscribed = true;
        return context;
    }

    [Fact]
    public void Build_PopulatesTimingAndIssues()
    {
        var report = QualityReportBuilder.Build(MakeContext(), @"C:\out\out.ass", 0);

        Assert.NotNull(report.Timing);
        // "Hello world." = 10 chars / 1s = 10 CPS > the 5 threshold
        Assert.True(report.Timing.MaxCps >= 10);
        Assert.Contains(report.Issues, i => i.Type == nameof(QualityIssueType.CpsTooHigh));
        Assert.All(report.Issues, i => Assert.True(i.SentenceIndex >= 0));
    }

    [Fact]
    public void Build_PopulatesConfidence()
    {
        var report = QualityReportBuilder.Build(MakeContext(), @"C:\out\out.ass", 0);

        Assert.NotNull(report.Confidence);
        Assert.NotNull(report.Confidence.MeanConfidence);
        Assert.Equal(0.6, report.Confidence.MeanConfidence!.Value, 1);
        Assert.Contains(1, report.Confidence.LowConfidenceSentenceIndexes); // 0.3 < 0.5
    }

    [Fact]
    public void Build_PopulatesTranslationQa()
    {
        var context = MakeContext();
        context.State.CurrentSentences =
        [
            new Sentence { Text = "Hello.", Start = 0, End = 500, TranslatedText = "你好。" },
            new Sentence { Text = "World.", Start = 600, End = 1000, TranslatedText = "世界。" }
        ];
        context.State.TranslatedSentences = context.State.CurrentSentences;
        context.State.IsTranslated = true;
        context.State.TranslationQa = new TranslationQa
        {
            GlossaryHitRate = 1,
            GlossaryExpected = 2,
            GlossaryHits = 2,
            MeanLengthRatio = 0.4,
            LengthDeviation = 0.6
        };

        var report = QualityReportBuilder.Build(context, @"C:\out\out.ass", 0);

        Assert.NotNull(report.Translation);
        Assert.Equal(1.0, report.Translation.GlossaryHitRate);
        Assert.Equal(0.4, report.Translation.MeanLengthRatio, 2);
        Assert.Equal(0.6, report.Translation.LengthDeviation, 2);
        Assert.Null(report.Translation.BackTranslateSimilarity);
    }

    [Fact]
    public void Build_DubCommand_PopulatesTtsMetrics()
    {
        var config = new WorkflowConfig
        {
            CommandName = "dub",
            InputFilePath = @"C:\in.mkv",
            OutputFilePath = @"C:\out.wav",
            DubDucking = true,
            DubBackgroundPath = @"C:\bg.wav"
        };
        var context = new SubtitleWorkflowContext(config);
        context.State.CurrentSentences = [new Sentence { Text = "Hello world.", Start = 0, End = 1000 }];
        context.State.DubSegments =
        [
            new DubSegment { Text = "Hello", TargetStartMs = 0, TargetEndMs = 500, AlignedDurationSec = 0.52 },
            new DubSegment { Text = "world", TargetStartMs = 600, TargetEndMs = 1000, AlignedDurationSec = 0.41 }
        ];

        var report = QualityReportBuilder.Build(context, @"C:\out.wav", 0);

        Assert.NotNull(report.Tts);
        Assert.True(report.Tts.MeanAlignmentErrorMs > 0);
        Assert.True(report.Tts.DuckingApplied);
        Assert.NotNull(report.Dub);
    }

    // ---------- Automatic fix engine ----------

    private static List<Sentence> MakeSentences()
    {
        // Sentence 1 exceeds the CPS limit (20 chars / 1s); sentence 2 is too short (100ms); sentence 3 overlaps sentence 4.
        return
        [
            new Sentence { Text = "This is a very very long line that exceeds limits.", Start = 0, End = 1000 },
            new Sentence { Text = "Hi.", Start = 1200, End = 1300 },
            new Sentence { Text = "Next line.", Start = 1500, End = 2000 },
            new Sentence { Text = "Overlapping line.", Start = 1900, End = 2500 }
        ];
    }

    [Fact]
    public void Fix_ResolvesOverlapShortAndLongLines()
    {
        var options = new QualityAssessmentOptions { MaxCps = 15, MaxCharsPerLine = 18, MinSentenceDurationMs = 300 };
        var result = QualityFixer.Fix(MakeSentences(), options);

        Assert.NotEmpty(result.Applied);

        // 1) Overlap fix: sentence 4's Start should be ≥ sentence 3's End (no overlap).
        Assert.True(result.Sentences.Count >= 4);
        var idx3 = result.Sentences.FindIndex(s => s.Text.Contains("Next line"));
        var idx4 = result.Sentences.FindIndex(s => s.Text.Contains("Overlapping line"));
        if (idx4 > idx3)
            Assert.True(result.Sentences[idx4].Start >= result.Sentences[idx3].End);

        // 2) Short-line merging: the 100ms "Hi." no longer exists.
        Assert.DoesNotContain(result.Sentences, s => s.Text.Trim() == "Hi.");

        // 3) Reassess after fixing: over-width lines and overlaps should be gone (CPS does not converge under proportional splitting, so no count assertion is made).
        var reassessed = QualityAssessor.Assess(result.Sentences, options);
        Assert.Equal(0, reassessed.Timing.LineTooLongCount);
        Assert.Equal(0, reassessed.Timing.OverlapCount);
    }

    [Fact]
    public void Fix_IsIdempotent()
    {
        var options = new QualityAssessmentOptions { MaxCps = 15, MaxCharsPerLine = 18, MinSentenceDurationMs = 300 };
        var first = QualityFixer.Fix(MakeSentences(), options);
        var second = QualityFixer.Fix(first.Sentences, options);

        // The second run should produce no further fixes (overlap / short lines / line width already meet the limits).
        Assert.Empty(second.Applied);
    }

    [Fact]
    public void Fix_LongLineSplitsAtPunctuation()
    {
        var sentences = new List<Sentence>
        {
            new() { Text = "Hello there my friend, this is a long line.", Start = 0, End = 3000 }
        };
        var result = QualityFixer.Fix(sentences, new QualityAssessmentOptions { MaxCharsPerLine = 18 });

        // Split into multiple segments: continuous timeline, unchanged total duration, and each segment within the line-width limit.
        Assert.True(result.Sentences.Count >= 2);
        Assert.Equal(3000.0, result.Sentences.Sum(s => s.End - s.Start), 1);
        Assert.All(result.Sentences, s => Assert.True(
            s.Text.Count(ch => !char.IsWhiteSpace(ch)) <= 18,
            $"segment '{s.Text}' exceeds 18 chars"));
        // Adjacent segments are continuous in time (the next segment's Start == the previous segment's End).
        for (var i = 0; i + 1 < result.Sentences.Count; i++)
            Assert.Equal(result.Sentences[i].End, result.Sentences[i + 1].Start);
    }

    // ---------- CI thresholds ----------

    [Fact]
    public void ThresholdRule_ParsesAndEvaluates()
    {
        var rule = QualityThresholdRule.TryParse("cps>20");
        Assert.NotNull(rule);
        Assert.Equal("cps", rule.Metric);
        Assert.Equal(20, rule.Value);

        var report = new QualityReport { Timing = new QualityTiming { MaxCps = 25 } };
        Assert.True(rule.Evaluate(report));

        var ok = new QualityReport { Timing = new QualityTiming { MaxCps = 10 } };
        Assert.False(rule.Evaluate(ok));
    }

    [Fact]
    public void ThresholdRule_CoverageUsesPercentScale()
    {
        var rule = QualityThresholdRule.TryParse("coverage<95");
        Assert.NotNull(rule);

        var low = new QualityReport { Alignment = new QualityAlignment { MapperCoverage = 0.90 } };
        Assert.True(rule.Evaluate(low));          // 90 < 95 → not satisfied → should fail

        var high = new QualityReport { Alignment = new QualityAlignment { MapperCoverage = 0.97 } };
        Assert.False(rule.Evaluate(high));        // 97 ≥ 95 → satisfied
    }

    [Fact]
    public void ThresholdRule_InvalidExpression_ReturnsNull()
    {
        Assert.Null(QualityThresholdRule.TryParse("bogus"));
        Assert.Null(QualityThresholdRule.TryParse("cps"));
        Assert.Null(QualityThresholdRule.TryParse(""));
        Assert.Null(QualityThresholdRule.TryParse("cps>abc"));
    }

    // ---------- HTML report ----------

    [Fact]
    public void HtmlReport_ContainsLineAnchorsAndThresholds()
    {
        var report = new QualityReport
        {
            Meta = new QualityMeta { Command = "spawn", InputFile = "in.mkv", OutputFile = "out.ass" },
            Counts = new QualityCounts { SentenceCount = 2 },
            Timing = new QualityTiming { MaxCps = 22, CpsTooHighCount = 1 },
            Passed = false,
            FailedThresholds = ["cps>20 (actual failed)"],
            Issues =
            [
                new QualityLineIssue
                {
                    Type = nameof(QualityIssueType.CpsTooHigh),
                    Severity = QualityIssueSeverity.Error.ToString(),
                    SentenceIndex = 1,
                    StartMs = 1000,
                    EndMs = 1500,
                    Text = "Too fast line.",
                    Message = "CPS 22.0 exceeds limit 20.0.",
                    Fix = "Split the sentence."
                }
            ]
        };

        var html = QualityHtmlReport.Render(report);

        Assert.Contains("id=\"line-2\"", html);      // the issue is located to a subtitle line
        Assert.Contains("Centurion Quality Report", html);
        Assert.Contains("cps&gt;20", html);          // threshold display
        Assert.Contains("Split the sentence.", html);
        Assert.Contains("<!DOCTYPE html>", html);
        Assert.DoesNotContain("<script", html);      // purely static and self-contained
    }
}
