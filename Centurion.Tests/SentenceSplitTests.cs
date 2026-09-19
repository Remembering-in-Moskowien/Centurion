using Centurion.Abstractions.Strategy;
using Centurion.Core.Strategy.SentenceSplit;
using Centurion.Models;
using Xunit;

namespace Centurion.Tests;

public sealed class SentenceSplitTests
{
    private static readonly SplitOptions Options = new()
    {
        MaxLength = 80,
        TargetLength = 50
    };

    [Fact]
    public async Task RuleBasedSplit_ForcesBreakOnSpeakerChange()
    {
        var words = new List<Word>
        {
            new() { Text = "Hello", Start = 0, End = 500, Speaker = "speaker 0" },
            new() { Text = "world,", Start = 500, End = 1000, Speaker = "speaker 0" },
            new() { Text = "Goodbye", Start = 1100, End = 1600, Speaker = "speaker 1" },
            new() { Text = "now!", Start = 1600, End = 2100, Speaker = "speaker 1" }
        };

        var strategy = new RuleBasedSplitStrategy();
        var sentences = await strategy.Split(words, Options);

        Assert.Equal(2, sentences.Count);
        Assert.Equal("Hello world,", sentences[0].Text);
        Assert.Equal("speaker 0", sentences[0].Words[0].Speaker);
        Assert.Equal("Goodbye now!", sentences[1].Text);
        Assert.Equal(1100, sentences[1].Start);
        Assert.Equal(2100, sentences[1].End);
    }

    [Fact]
    public async Task RuleBasedSplit_IgnoresDefaultSpeakerLabels()
    {
        var words = new List<Word>
        {
            new() { Text = "Hello", Start = 0, End = 500, Speaker = "SPEAKER_00" },
            new() { Text = "world", Start = 500, End = 1000, Speaker = "SPEAKER_00" },
            new() { Text = "again", Start = 1100, End = 1600, Speaker = "SPEAKER_00" }
        };

        var strategy = new RuleBasedSplitStrategy();
        var sentences = await strategy.Split(words, Options);

        // 全部为回退标签 → 不触发说话人断句（无标点 → 整段一句）
        Assert.Single(sentences);
        Assert.Equal("Hello world again", sentences[0].Text);
    }

    [Fact]
    public async Task RuleBasedSplit_MixedLabels_DoNotSplitAtUnknown()
    {
        var words = new List<Word>
        {
            new() { Text = "Hello", Start = 0, End = 500, Speaker = "speaker 0" },
            new() { Text = "there", Start = 500, End = 1000, Speaker = "SPEAKER_00" },
            new() { Text = "friend", Start = 1100, End = 1600, Speaker = "speaker 1" }
        };

        var strategy = new RuleBasedSplitStrategy();
        var sentences = await strategy.Split(words, Options);

        // SPEAKER_00 是未命中回退标签，与其相邻的有效标签不触发说话人强制断句
        Assert.Single(sentences);
        Assert.Equal("Hello there friend", sentences[0].Text);
    }
}
