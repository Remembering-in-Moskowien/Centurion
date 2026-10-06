using Centurion.Abstractions.Strategy;
using Centurion.Core.Workflow.Strategy.SentenceSplit;using Centurion.Models;
using Xunit;

namespace Centurion.Tests.Core;

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

        var strategy = new AggressiveRuleSplitStrategy();
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

        var strategy = new AggressiveRuleSplitStrategy();
        var sentences = await strategy.Split(words, Options);

        // All fallback labels → no speaker-based split is triggered (no punctuation → the whole segment is one sentence).
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

        var strategy = new AggressiveRuleSplitStrategy();
        var sentences = await strategy.Split(words, Options);

        // SPEAKER_00 is an unmatched fallback label, so it does not trigger a forced speaker break with the valid labels around it.
        Assert.Single(sentences);
        Assert.Equal("Hello there friend", sentences[0].Text);
    }

    [Fact]
    public async Task RuleBasedSplit_SplitsDenseDialogueOnPausesWithoutPunctuation()
    {
        // Short, dense dialogue: punctuation-free transcription with noticeable pauses between turns (500ms) and minimal within-word gaps.
        var words = new List<Word>
        {
            new() { Text = "Hi", Start = 0, End = 300, Speaker = "SPEAKER_00" },
            new() { Text = "there", Start = 800, End = 1100, Speaker = "SPEAKER_00" },
            new() { Text = "How", Start = 1100, End = 1400, Speaker = "SPEAKER_00" },
            new() { Text = "are", Start = 1400, End = 1700, Speaker = "SPEAKER_00" },
            new() { Text = "you", Start = 1700, End = 2000, Speaker = "SPEAKER_00" }
        };

        var strategy = new AggressiveRuleSplitStrategy();
        var sentences = await strategy.Split(words, Options);

        // A forced break occurs at the pause (after Hi), while the remaining continuous words stay in one sentence.
        Assert.Equal(2, sentences.Count);
        Assert.Equal("Hi", sentences[0].Text);
        Assert.Equal("there How are you", sentences[1].Text);
    }

    [Fact]
    public async Task RuleBasedSplit_SplitsOnPausesBetweenShortTurns()
    {
        // Two short turns: "Hello world" / "Good morning", with a 600ms pause between them and no punctuation.
        var words = new List<Word>
        {
            new() { Text = "Hello", Start = 0, End = 300, Speaker = "SPEAKER_00" },
            new() { Text = "world", Start = 300, End = 600, Speaker = "SPEAKER_00" },
            new() { Text = "Good", Start = 1200, End = 1500, Speaker = "SPEAKER_00" },
            new() { Text = "morning", Start = 1500, End = 1800, Speaker = "SPEAKER_00" }
        };

        var strategy = new AggressiveRuleSplitStrategy();
        var sentences = await strategy.Split(words, Options);

        Assert.Equal(2, sentences.Count);
        Assert.Equal("Hello world", sentences[0].Text);
        Assert.Equal("Good morning", sentences[1].Text);
        Assert.Equal(600, sentences[0].End);
        Assert.Equal(1200, sentences[1].Start);
    }

    [Fact]
    public async Task RuleBasedSplit_KeepsUniformFastSpeechTogether()
    {
        // Uniform continuous speech: even inter-word gaps (100ms), no punctuation and no noticeable pauses → the whole segment stays one sentence.
        var words = new List<Word>
        {
            new() { Text = "This", Start = 0, End = 200, Speaker = "SPEAKER_00" },
            new() { Text = "is", Start = 300, End = 500, Speaker = "SPEAKER_00" },
            new() { Text = "a", Start = 600, End = 800, Speaker = "SPEAKER_00" },
            new() { Text = "test", Start = 900, End = 1100, Speaker = "SPEAKER_00" }
        };

        var strategy = new AggressiveRuleSplitStrategy();
        var sentences = await strategy.Split(words, Options);

        Assert.Single(sentences);
        Assert.Equal("This is a test", sentences[0].Text);
    }

    [Fact]
    public async Task RuleBasedSplit_PunctuationBreaksRemainMandatory()
    {
        // Punctuation remains the highest-priority hard break: a forced break after a full stop.
        var words = new List<Word>
        {
            new() { Text = "Hello", Start = 0, End = 300, Speaker = "SPEAKER_00" },
            new() { Text = "world.", Start = 300, End = 600, Speaker = "SPEAKER_00" },
            new() { Text = "Goodbye", Start = 600, End = 900, Speaker = "SPEAKER_00" },
            new() { Text = "now.", Start = 900, End = 1200, Speaker = "SPEAKER_00" }
        };

        var strategy = new AggressiveRuleSplitStrategy();
        var sentences = await strategy.Split(words, Options);

        Assert.Equal(2, sentences.Count);
        Assert.Equal("Hello world.", sentences[0].Text);
        Assert.Equal("Goodbye now.", sentences[1].Text);
    }

    [Fact]
    public async Task RuleBasedSplit_OversizedSegmentSplitsByLength()
    {
        // A long segment with no punctuation or pauses (exceeds MaxLength): split by length DP, with no sentence over the limit.
        var words = new List<Word>();
        var t = 0;
        for (var i = 0; i < 30; i++)
        {
            words.Add(new Word { Text = "word", Start = t, End = t + 100, Speaker = "SPEAKER_00" });
            t += 100;
        }

        var strategy = new AggressiveRuleSplitStrategy();
        var sentences = await strategy.Split(words, Options);

        Assert.True(sentences.Count >= 2, "oversized segment should be split");
        foreach (var s in sentences)
            Assert.True(s.Text.Length <= Options.MaxLength, $"sentence too long: {s.Text}");
    }

    [Fact]
    public async Task RuleBasedSplit_DoesNotIsolatePunctuationWordAfterPause()
    {
        // An in-sentence breath pause (320ms) followed by a final punctuation word ("waiting."):
        // the pause break should be suppressed (the real sentence boundary is after the punctuation word), to avoid isolated final words causing zero-duration sentences and lost words.
        var words = new List<Word>
        {
            new() { Text = "after", Start = 0, End = 300, Speaker = "SPEAKER_00" },
            new() { Text = "like", Start = 300, End = 600, Speaker = "SPEAKER_00" },
            new() { Text = "three", Start = 600, End = 900, Speaker = "SPEAKER_00" },
            new() { Text = "weeks", Start = 900, End = 1200, Speaker = "SPEAKER_00" },
            new() { Text = "of", Start = 1200, End = 1500, Speaker = "SPEAKER_00" },
            new() { Text = "waiting.", Start = 1820, End = 2120, Speaker = "SPEAKER_00" },  // in-sentence 320ms breath pause
            new() { Text = "So", Start = 2440, End = 2740, Speaker = "SPEAKER_00" },        // inter-sentence 320ms pause
            new() { Text = "we're", Start = 2740, End = 3040, Speaker = "SPEAKER_00" }
        };

        var strategy = new AggressiveRuleSplitStrategy();
        var sentences = await strategy.Split(words, Options);

        Assert.Equal(2, sentences.Count);
        Assert.Equal("after like three weeks of waiting.", sentences[0].Text);
        Assert.Equal("So we're", sentences[1].Text);
    }

    [Fact]
    public async Task RuleBasedSplit_DoesNotIsolateCommaWordAfterPause()
    {
        // A 320ms pause precedes the comma word ("hundred,"): it is not isolated — "is over a hundred," stays one sentence.
        var words = new List<Word>
        {
            new() { Text = "my", Start = 0, End = 200, Speaker = "SPEAKER_00" },
            new() { Text = "FPS", Start = 200, End = 400, Speaker = "SPEAKER_00" },
            new() { Text = "is", Start = 400, End = 600, Speaker = "SPEAKER_00" },
            new() { Text = "over", Start = 600, End = 800, Speaker = "SPEAKER_00" },
            new() { Text = "a", Start = 800, End = 1000, Speaker = "SPEAKER_00" },
            new() { Text = "hundred,", Start = 1320, End = 1620, Speaker = "SPEAKER_00" },
            new() { Text = "which", Start = 1620, End = 1820, Speaker = "SPEAKER_00" }
        };

        var strategy = new AggressiveRuleSplitStrategy();
        var sentences = await strategy.Split(words, Options);

        Assert.Equal(2, sentences.Count);
        Assert.Equal("my FPS is over a hundred,", sentences[0].Text);
        Assert.Equal("which", sentences[1].Text);
    }

    [Fact]
    public async Task RuleBasedSplit_PreservesAllWordsAcrossBreaks()
    {
        // Word integrity: for input mixing punctuation / pauses / oversized segments, after splitting every word must appear in the output sentences,
        // with identical order, reference identity, and text (no lost words, no lost order).
        var words = new List<Word>();
        var t = 0;
        foreach (var (text, gapBeforeMs) in new (string, int)[]
        {
            ("Hello", 0), ("world", 0), (".", 0),              // punctuation break
            ("This", 700), ("is", 0), ("a", 0),                // pause break
            ("test", 0), ("with", 0), ("many", 0), ("words", 0),
            ("inside", 0), ("one", 0), ("long", 0), ("sentence", 0),
            ("okay", 900)                                       // pause before the last word
        })
        {
            t += gapBeforeMs;
            words.Add(new Word { Text = text, Start = t, End = t + 200, Speaker = "SPEAKER_00" });
            t += 200;
        }

        var strategy = new AggressiveRuleSplitStrategy();
        var sentences = await strategy.Split(words, Options);

        var outputWords = sentences.SelectMany(s => s.Words).ToList();
        Assert.Equal(words.Count, outputWords.Count);
        for (var i = 0; i < words.Count; i++)
        {
            Assert.Same(words[i], outputWords[i]);
            Assert.Equal(words[i].Text, outputWords[i].Text);
        }

        // Each sentence's text is non-empty and contains all of its words (punctuation words are intelligently joined by JoinWords; space differences are ignored).
        foreach (var s in sentences)
        {
            Assert.False(string.IsNullOrWhiteSpace(s.Text));
            foreach (var w in s.Words)
                Assert.Contains(w.Text, s.Text, StringComparison.Ordinal);
        }
    }
}
