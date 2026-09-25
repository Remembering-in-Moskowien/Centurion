using Centurion.Core.Utils;
using Centurion.Models;
using Centurion.Models.Workflow;
using Xunit;

namespace Centurion.Tests;

/// <summary>
/// 质量报告构建器测试：验证从工作流上下文提取的指标正确性（单位、说话人、覆盖标志）。
/// </summary>
public class QualityReportBuilderTests
{
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
            new Sentence { Text = "Hello world.", Start = 0, End = 1000, Words = [new Word { Text = "Hello", Start = 0, End = 400, Speaker = "SPEAKER_01" }, new Word { Text = "world.", Start = 400, End = 1000, Speaker = "SPEAKER_01" }] },
            new Sentence { Text = "Goodbye.", Start = 1500, End = 2000, Words = [new Word { Text = "Goodbye.", Start = 1500, End = 2000, Speaker = "SPEAKER_02" }] }
        ];
        context.State.CurrentSentences = context.State.TranscribeSentences;
        context.State.IsTranscribed = true;
        context.State.IsDiarized = true;
        return context;
    }

    [Fact]
    public void Build_CountsAndDuration_AreCorrect()
    {
        var report = QualityReportBuilder.Build(MakeContext(), @"C:\out\out.ass", 12.5);

        Assert.Equal("spawn", report.Meta.Command);
        Assert.Equal(12.5, report.Meta.ElapsedSeconds);
        Assert.Equal(2, report.Counts.SentenceCount);
        Assert.Equal(3, report.Counts.WordCount);
        Assert.Equal(19, report.Counts.CharacterCount);   // "Helloworld."(11) + "Goodbye."(8)
        Assert.Equal(2, report.Counts.SpeakerCount);
        Assert.Equal(2.0, report.Counts.DurationSeconds); // (2000-0)/1000
    }

    [Fact]
    public void Build_CoverageFlags_FollowState()
    {
        var report = QualityReportBuilder.Build(MakeContext(), @"C:\out\out.ass", 0);

        Assert.True(report.Coverage.Transcribed);
        Assert.True(report.Coverage.Diarized);
        Assert.False(report.Coverage.Aligned);
        Assert.Equal(2, report.Coverage.TranscribedSentenceCount);
    }

    [Fact]
    public void Build_ZeroDurationSentences_Counted()
    {
        var context = MakeContext();
        context.State.CurrentSentences.Add(new Sentence { Text = "Zero.", Start = 2000, End = 2000 });

        var report = QualityReportBuilder.Build(context, @"C:\out\out.ass", 0);

        Assert.Equal(1, report.Alignment.ZeroDurationSentenceCount);
        Assert.Equal(3, report.Counts.SentenceCount);
    }

    [Fact]
    public void Build_EmptySentences_NoCrash()
    {
        var config = new WorkflowConfig { CommandName = "convert", InputFilePath = @"C:\in.srt", OutputFilePath = @"C:\out.ass" };
        var context = new SubtitleWorkflowContext(config);

        var report = QualityReportBuilder.Build(context, @"C:\out.ass", 0);

        Assert.Equal(0, report.Counts.SentenceCount);
        Assert.Equal(0, report.Counts.SpeakerCount);
        Assert.Equal(0.0, report.Counts.DurationSeconds);
    }

    [Fact]
    public void Build_CorrectionReportDrift_PopulatesAlignment()
    {
        var context = MakeContext();
        context.State.Report = new CorrectionReport { AverageDriftMs = 120.5, MaxDriftMs = 340.0, TextCoverage = 0.96 };

        var report = QualityReportBuilder.Build(context, @"C:\out\out.ass", 0);

        Assert.Equal(120.5, report.Alignment.MeanDriftMs);
        Assert.Equal(340.0, report.Alignment.MaxDriftMs);
        Assert.Equal(0.96, report.Alignment.MapperCoverage);
    }
}
