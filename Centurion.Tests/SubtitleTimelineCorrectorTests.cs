using Centurion.Core.Models;
using Centurion.Core.PipeLine;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Centurion.Tests;

public sealed class SubtitleTimelineCorrectorTests
{
    [Fact]
    public async Task Corrector_RetriesWithExpandedWindow()
    {
        var subtitle = new Sentence { Text = "hello", Start = 0, End = 1000 };
        var context = CreateContext(subtitle, new Word
        {
            Text = "hello",
            Start = 5000,
            End = 5200,
            Speaker = "UNKNOWN"
        });

        var operatorInstance = new SubtitleTimelineCorrectorOp(NullLogger<SubtitleTimelineCorrectorOp>.Instance);
        await operatorInstance.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(5000, context.State.CurrentSentences.Single().Start);
        Assert.Equal(5200, context.State.CurrentSentences.Single().End);
    }

    [Fact]
    public async Task Corrector_DoesNotInsertSpontaneousSentenceForNearbyNoiseWords()
    {
        var subtitle = new Sentence { Text = "hello", Start = 1000, End = 1500 };
        var context = CreateContext(
            subtitle,
            new Word { Text = "um", Start = 0, End = 50, Speaker = "UNKNOWN" },
            new Word { Text = "well", Start = 100, End = 150, Speaker = "UNKNOWN" },
            new Word { Text = "actually", Start = 200, End = 250, Speaker = "UNKNOWN" },
            new Word { Text = "hello", Start = 1000, End = 1200, Speaker = "UNKNOWN" });

        var operatorInstance = new SubtitleTimelineCorrectorOp(NullLogger<SubtitleTimelineCorrectorOp>.Instance);
        await operatorInstance.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(1, context.State.CurrentSentences.Count);
        Assert.DoesNotContain(context.State.CurrentSentences, sentence => sentence.Text.StartsWith("[SPONT]", StringComparison.Ordinal));
    }

    private static SubtitleWorkflowContext CreateContext(Sentence subtitle, params Word[] words)
    {
        return new SubtitleWorkflowContext(new WorkflowConfig { FuzzyThreshold = 0.72 })
        {
            State = new WorkflowState
            {
                CurrentSentences = [subtitle],
                TranscribeSentences = [new Sentence { Text = string.Join(" ", words.Select(word => word.Text)), Words = [.. words] }],
                SubtitleSentences = [new Sentence { Text = subtitle.Text, Start = subtitle.Start, End = subtitle.End }]
            }
        };
    }
}
