using Centurion.Abstractions.Strategy;
using Centurion.Core.Workflow.Strategy.SentenceSplit;using Centurion.Models;
using Xunit;

namespace Centurion.Tests.Core;

/// <summary>
/// 消极规则分句策略测试（独白等匀速连续语音）：只认标点断句，
/// 不因词间换气停顿切句；超长无标点段由全局 DP 均匀切分。
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
        // 独白：三个句号 + 一个逗号，词间间隙均匀（100ms）；总长超 MaxLength（80），
        // 全局 DP 只能在标点处断开以满足长度约束——标点是唯一断点候选
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
        // 每句不超 MaxLength
        foreach (var s in sentences)
            Assert.True(s.Text.Length <= Options.MaxLength, $"sentence too long: {s.Text}");
        // 全部在标点后断开（句末词以标点结尾或为最后一句）
        foreach (var s in sentences)
            Assert.True(
                s.Text.EndsWith('.') || s.Text.EndsWith(',') || ReferenceEquals(s, sentences[^1]),
                $"break not at punctuation: {s.Text}");
        // 词完整性
        Assert.Equal(words.Count, sentences.SelectMany(s => s.Words).Count());
    }

    [Fact]
    public async Task PassiveSplit_KeepsNaturalPausesTogether_UnlikeAggressive()
    {
        // 独白换气停顿（500ms）但无标点：消极档不因停顿断句，整段保持一句（积极档会在此断开）
        var words = new List<Word>
        {
            new() { Text = "In", Start = 0, End = 200, Speaker = "SPEAKER_00" },
            new() { Text = "the", Start = 200, End = 400, Speaker = "SPEAKER_00" },
            new() { Text = "beginning", Start = 400, End = 600, Speaker = "SPEAKER_00" },
            new() { Text = "there", Start = 1100, End = 1300, Speaker = "SPEAKER_00" }, // 500ms 换气
            new() { Text = "was", Start = 1300, End = 1500, Speaker = "SPEAKER_00" },
            new() { Text = "only", Start = 1500, End = 1700, Speaker = "SPEAKER_00" },
            new() { Text = "silence", Start = 1700, End = 1900, Speaker = "SPEAKER_00" }
        };

        var passive = new PassiveRuleSplitStrategy();
        var passiveSentences = await passive.Split(words, Options);

        // 无标点 → 整段一句（自然停顿不触发切分）
        Assert.Single(passiveSentences);
        Assert.Equal("In the beginning there was only silence", passiveSentences[0].Text);

        // 对照组：积极档会在 500ms 停顿处断开
        var aggressive = new AggressiveRuleSplitStrategy();
        var aggressiveSentences = await aggressive.Split(words, Options);
        Assert.True(aggressiveSentences.Count >= 2, "aggressive should split on the pause");
    }

    [Fact]
    public async Task PassiveSplit_OversizedNoPunctuation_SplitsByLengthUniformly()
    {
        // 独白超长无标点段（超 MaxLength）：全局 DP 切分，每句不超限
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

        // 词完整性：全部词保序出现
        var outputWords = sentences.SelectMany(s => s.Words).ToList();
        Assert.Equal(words.Count, outputWords.Count);
    }

    [Fact]
    public async Task PassiveSplit_ForcesBreakOnSpeakerChange()
    {
        // 说话人切换仍是强制断点（两档通用行为）
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
        // 全部回退标签 → 无标点 → 整段一句（不触发说话人断句）
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
