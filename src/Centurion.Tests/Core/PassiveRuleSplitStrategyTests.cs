using Centurion.Abstractions.Strategy;
using Centurion.Core.Workflow.Strategy.SentenceSplit;using Centurion.Models;
using Xunit;

namespace Centurion.Tests.Core;

/// <summary>
/// Tests for the passive rule-based sentence-splitting strategy (monologue-like, evenly paced continuous speech): it only breaks at punctuation,
/// never splits on inter-word breath pauses; oversized punctuation-free segments are split uniformly by a global DP.
/// </summary>
public sealed class PassiveRuleSplitStrategyTests
{
    private static readonly SplitOptions Options = new()
    {
        MaxLength = 80,
        TargetLength = 50
    };

    [Fact]
    public async Task PassiveSplit_BreaksAtPunctuation_WhenOversized()
    {
        // Monologue: three full stops plus one comma, with even inter-word gaps (100ms); the total exceeds MaxLength (80),
        // so the global DP can only break at punctuation to satisfy the length limit — punctuation is the only break candidate.
        var words = new List<Word>
        {
            new() { Text = "This", Start = 0, End = 200, Speaker = "SPEAKER_00" },
            new() { Text = "is", Start = 300, End = 500, Speaker = "SPEAKER_00" },
            new() { Text = "a", Start = 600, End = 800, Speaker = "SPEAKER_00" },
            new() { Text = "very", Start = 900, End = 1100, Speaker = "SPEAKER_00" },
            new() { Text = "long", Start = 1200, End = 1400, Speaker = "SPEAKER_00" },
            new() { Text = "monologue,", Start = 1500, End = 1700, Speaker = "SPEAKER_00" },
            new() { Text = "with", Start = 1800, End = 2000, Speaker = "SPEAKER_00" },
            new() { Text = "many", Start = 2100, End = 2300, Speaker = "SPEAKER_00" },
            new() { Text = "words", Start = 2400, End = 2600, Speaker = "SPEAKER_00" },
            new() { Text = "that", Start = 2700, End = 2900, Speaker = "SPEAKER_00" },
            new() { Text = "need", Start = 3000, End = 3200, Speaker = "SPEAKER_00" },
            new() { Text = "breaking", Start = 3300, End = 3500, Speaker = "SPEAKER_00" },
            new() { Text = "up.", Start = 3600, End = 3800, Speaker = "SPEAKER_00" },
            new() { Text = "It", Start = 3900, End = 4100, Speaker = "SPEAKER_00" },
            new() { Text = "continues", Start = 4200, End = 4400, Speaker = "SPEAKER_00" },
            new() { Text = "even", Start = 4500, End = 4700, Speaker = "SPEAKER_00" },
            new() { Text = "further", Start = 4800, End = 5000, Speaker = "SPEAKER_00" },
            new() { Text = "with", Start = 5100, End = 5300, Speaker = "SPEAKER_00" },
            new() { Text = "more", Start = 5400, End = 5600, Speaker = "SPEAKER_00" },
            new() { Text = "content.", Start = 5700, End = 5900, Speaker = "SPEAKER_00" }
        };

        var strategy = new PassiveRuleSplitStrategy();
        var sentences = await strategy.Split(words, Options);

        Assert.True(sentences.Count >= 2, $"expected >=2 sentences, got {sentences.Count}");
        // No sentence exceeds MaxLength.
        foreach (var s in sentences)
            Assert.True(s.Text.Length <= Options.MaxLength, $"sentence too long: {s.Text}");
        // All breaks fall after punctuation (the last word ends with punctuation, or it is the final sentence).
        foreach (var s in sentences)
            Assert.True(
                s.Text.EndsWith('.') || s.Text.EndsWith(',') || ReferenceEquals(s, sentences[^1]),
                $"break not at punctuation: {s.Text}");
        // Word integrity.
        Assert.Equal(words.Count, sentences.SelectMany(s => s.Words).Count());
    }

    [Fact]
    public async Task PassiveSplit_KeepsNaturalPausesTogether_UnlikeAggressive()
    {
        // A monologue breath pause (500ms) with no punctuation: the passive profile does not break on the pause, keeping the whole segment as one sentence (the aggressive profile would break here).
        var words = new List<Word>
        {
            new() { Text = "In", Start = 0, End = 200, Speaker = "SPEAKER_00" },
            new() { Text = "the", Start = 200, End = 400, Speaker = "SPEAKER_00" },
            new() { Text = "beginning", Start = 400, End = 600, Speaker = "SPEAKER_00" },
            new() { Text = "there", Start = 1100, End = 1300, Speaker = "SPEAKER_00" }, // 500ms breath pause
            new() { Text = "was", Start = 1300, End = 1500, Speaker = "SPEAKER_00" },
            new() { Text = "only", Start = 1500, End = 1700, Speaker = "SPEAKER_00" },
            new() { Text = "silence", Start = 1700, End = 1900, Speaker = "SPEAKER_00" }
        };

        var passive = new PassiveRuleSplitStrategy();
        var passiveSentences = await passive.Split(words, Options);

        // No punctuation → the whole segment is one sentence (natural pauses do not trigger splitting).
        Assert.Single(passiveSentences);
        Assert.Equal("In the beginning there was only silence", passiveSentences[0].Text);

        // Control group: the aggressive profile breaks at the 500ms pause.
        var aggressive = new AggressiveRuleSplitStrategy();
        var aggressiveSentences = await aggressive.Split(words, Options);
        Assert.True(aggressiveSentences.Count >= 2, "aggressive should split on the pause");
    }

    [Fact]
    public async Task PassiveSplit_OversizedNoPunctuation_SplitsByLengthUniformly()
    {
        // An oversized punctuation-free monologue segment (exceeds MaxLength): split by the global DP, with no sentence over the limit.
        var words = new List<Word>();
        var t = 0;
        for (var i = 0; i < 30; i++)
        {
            words.Add(new Word { Text = "word", Start = t, End = t + 100, Speaker = "SPEAKER_00" });
            t += 100;
        }

        var strategy = new PassiveRuleSplitStrategy();
        var sentences = await strategy.Split(words, Options);

        Assert.True(sentences.Count >= 2, "oversized segment should be split");
        foreach (var s in sentences)
            Assert.True(s.Text.Length <= Options.MaxLength, $"sentence too long: {s.Text}");

        // Word integrity: all words appear in order.
        var outputWords = sentences.SelectMany(s => s.Words).ToList();
        Assert.Equal(words.Count, outputWords.Count);
    }

    [Fact]
    public async Task PassiveSplit_ForcesBreakOnSpeakerChange()
    {
        // A speaker change remains a forced break point (behavior shared by both profiles).
        var words = new List<Word>
        {
            new() { Text = "Narrator", Start = 0, End = 500, Speaker = "speaker 0" },
            new() { Text = "speaks,", Start = 500, End = 1000, Speaker = "speaker 0" },
            new() { Text = "Other", Start = 1100, End = 1600, Speaker = "speaker 1" },
            new() { Text = "interjects.", Start = 1600, End = 2100, Speaker = "speaker 1" }
        };

        var strategy = new PassiveRuleSplitStrategy();
        var sentences = await strategy.Split(words, Options);

        Assert.Equal(2, sentences.Count);
        Assert.Equal("Narrator speaks,", sentences[0].Text);
        Assert.Equal("Other interjects.", sentences[1].Text);
    }

    [Fact]
    public async Task PassiveSplit_IgnoresDefaultSpeakerLabels()
    {
        // All fallback labels → no punctuation → the whole segment is one sentence (no speaker-based split is triggered).
        var words = new List<Word>
        {
            new() { Text = "Hello", Start = 0, End = 500, Speaker = "SPEAKER_00" },
            new() { Text = "world", Start = 500, End = 1000, Speaker = "SPEAKER_00" },
            new() { Text = "again", Start = 1100, End = 1600, Speaker = "SPEAKER_00" }
        };

        var strategy = new PassiveRuleSplitStrategy();
        var sentences = await strategy.Split(words, Options);

        Assert.Single(sentences);
        Assert.Equal("Hello world again", sentences[0].Text);
    }
}
