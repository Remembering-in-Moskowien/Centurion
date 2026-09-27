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

        var strategy = new AggressiveRuleSplitStrategy();
        var sentences = await strategy.Split(words, Options);

        // SPEAKER_00 是未命中回退标签，与其相邻的有效标签不触发说话人强制断句
        Assert.Single(sentences);
        Assert.Equal("Hello there friend", sentences[0].Text);
    }

    [Fact]
    public async Task RuleBasedSplit_SplitsDenseDialogueOnPausesWithoutPunctuation()
    {
        // 短促密集对话：无标点转录，句间有明显停顿（500ms），词内间隙极小
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

        // 停顿处（Hi 后）强制断句，其余连续词保持成句
        Assert.Equal(2, sentences.Count);
        Assert.Equal("Hi", sentences[0].Text);
        Assert.Equal("there How are you", sentences[1].Text);
    }

    [Fact]
    public async Task RuleBasedSplit_SplitsOnPausesBetweenShortTurns()
    {
        // 两短轮次："Hello world" / "Good morning"，中间停顿 600ms，无标点
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
        // 均匀连续语音：词间间隙一致（100ms），无标点无显著停顿 → 保持整段一句
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
        // 标点仍是最高优先级硬断点：句号后强制断句
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
        // 无标点、无停顿的长段（超 MaxLength）：按长度 DP 切分且每句不超限
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
        // 句内换气停顿（320ms）后接句末标点词（"waiting."）：
        // 停顿断点应被抑制（真正的句界在标点词之后），避免孤立句末词造成零时长句丢词
        var words = new List<Word>
        {
            new() { Text = "after", Start = 0, End = 300, Speaker = "SPEAKER_00" },
            new() { Text = "like", Start = 300, End = 600, Speaker = "SPEAKER_00" },
            new() { Text = "three", Start = 600, End = 900, Speaker = "SPEAKER_00" },
            new() { Text = "weeks", Start = 900, End = 1200, Speaker = "SPEAKER_00" },
            new() { Text = "of", Start = 1200, End = 1500, Speaker = "SPEAKER_00" },
            new() { Text = "waiting.", Start = 1820, End = 2120, Speaker = "SPEAKER_00" },  // 句内 320ms 换气
            new() { Text = "So", Start = 2440, End = 2740, Speaker = "SPEAKER_00" },        // 句间 320ms 停顿
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
        // 逗号词（"hundred,"）前有 320ms 停顿：不孤立——"is over a hundred," 保持一句
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
        // 词完整性：混合标点/停顿/超长段的输入，分句后所有词必须出现在输出句子中，
        // 且顺序、引用、文本一致（不丢词、不丢顺序）
        var words = new List<Word>();
        var t = 0;
        foreach (var (text, gapBeforeMs) in new (string, int)[]
        {
            ("Hello", 0), ("world", 0), (".", 0),              // 标点断点
            ("This", 700), ("is", 0), ("a", 0),                // 停顿断点
            ("test", 0), ("with", 0), ("many", 0), ("words", 0),
            ("inside", 0), ("one", 0), ("long", 0), ("sentence", 0),
            ("okay", 900)                                       // 末词前停顿
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

        // 每句文本非空且包含其全部词（标点词由 JoinWords 智能粘连，空格差异不计）
        foreach (var s in sentences)
        {
            Assert.False(string.IsNullOrWhiteSpace(s.Text));
            foreach (var w in s.Words)
                Assert.Contains(w.Text, s.Text, StringComparison.Ordinal);
        }
    }
}
