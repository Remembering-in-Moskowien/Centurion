using Centurion.Core.Models;
using Centurion.Core.PipeLine;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Centurion.Tests;

public sealed class SubtitleTextCorrectorTests
{
    [Fact]
    public async Task Corrector_ReplacesTextAndKeepsTimingAndWords()
    {
        var word = new Word { Text = "old", Start = 100, End = 200, Speaker = "UNKNOWN" };
        var subtitle = new Sentence
        {
            Text = "helo",
            Start = 100,
            End = 200,
            Words = [word]
        };
        var context = new SubtitleWorkflowContext(new WorkflowConfig { FuzzyThreshold = 0.7 })
        {
            State = new WorkflowState
            {
                CurrentSentences = [subtitle],
                ScriptSentences = [new Sentence { Text = "hello", CleanedText = "hello" }]
            }
        };

        var operatorInstance = new SubtitleTextCorrectorOp(NullLogger<SubtitleTextCorrectorOp>.Instance);
        await operatorInstance.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal("hello", subtitle.Text);
        Assert.Equal("hello", subtitle.CleanedText);
        Assert.Equal(100, subtitle.Start);
        Assert.Same(word, subtitle.Words[0]);
        Assert.Equal(1, context.State.Report.TextCorrected);
    }

    [Fact]
    public async Task Corrector_LeavesUnmatchedTextAndDoesNotAdvanceToLaterScriptLine()
    {
        var first = new Sentence { Text = "unrelated", Start = 0, End = 100 };
        var second = new Sentence { Text = "target", Start = 100, End = 200 };
        var context = new SubtitleWorkflowContext(new WorkflowConfig { FuzzyThreshold = 0.9 })
        {
            State = new WorkflowState
            {
                CurrentSentences = [first, second],
                ScriptSentences = [
                    new Sentence { Text = "different" },
                    new Sentence { Text = "target" }
                ]
            }
        };

        var operatorInstance = new SubtitleTextCorrectorOp(NullLogger<SubtitleTextCorrectorOp>.Instance);
        await operatorInstance.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal("unrelated", first.Text);
        Assert.Equal("target", second.Text);
        Assert.Equal(1, context.State.Report.TextCorrected);
    }
}
