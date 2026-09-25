using Centurion.Abstractions.Strategy;
using Centurion.Core.Strategy.SentenceSplit;
using Centurion.Core.Utils;
using Centurion.Models;
using Centurion.Models.Ass;
using Centurion.Models.Text;
using Xunit;

namespace Centurion.Tests;

/// <summary>
/// 多语言混合文本（code-switching）测试：英语听力材料中夹带中/日文时的
/// 混合感知分词、拼接、分句与卡拉OK时间戳构建。
/// </summary>
public sealed class MixedLanguageSupportTests
{
    // ---------- TokenizeMixed ----------

    [Theory]
    [InlineData("hello 世界 world", new[] { "hello", "世", "界", "world" })]
    [InlineData("我们talk about", new[] { "我", "们", "talk", "about" })]
    [InlineData("English 中文 mixed 测试", new[] { "English", "中", "文", "mixed", "测", "试" })]
    [InlineData("こんにちは world", new[] { "こ", "ん", "に", "ち", "は", "world" })]
    [InlineData("你好。", new[] { "你", "好。" })]
    [InlineData("Yes，当然", new[] { "Yes，", "当", "然" })]
    [InlineData("안녕하세요 world", new[] { "안녕하세요", "world" })]
    [InlineData("纯中文句子", new[] { "纯", "中", "文", "句", "子" })]
    [InlineData("", new string[0])]
    public void TokenizeMixed_SplitsByCharacterClass(string text, string[] expected)
        => Assert.Equal(expected, LanguageSupport.TokenizeMixed(text));

    // ---------- JoinMixed ----------

    [Fact]
    public void JoinMixed_CjkRunsConcatenate_LatinUsesSpaces()
        => Assert.Equal("我们 talk about", LanguageSupport.JoinMixed(["我", "们", "talk", "about"]));

    [Fact]
    public void JoinMixed_EnglishThenChinese_KeepsSingleSpace()
        => Assert.Equal("hello 世界", LanguageSupport.JoinMixed(["hello", "世", "界"]));

    [Fact]
    public void JoinMixed_CjkWithPunctuation_NoSpaceAround()
        => Assert.Equal("你好。", LanguageSupport.JoinMixed(["你", "好。"]));

    [Fact]
    public void JoinMixed_LatinWords_KeepSpaces()
        => Assert.Equal("a b c", LanguageSupport.JoinMixed(["a", "b", "c"]));

    [Fact]
    public void JoinMixed_Korean_KeepsWordSpaces()
        => Assert.Equal("안녕하세요 세계", LanguageSupport.JoinMixed(["안녕하세요", "세계"]));

    [Fact]
    public void JoinMixed_MixedCjkAndDigitTokens_StayTight()
        => Assert.Equal("字0字1", LanguageSupport.JoinMixed(["字0", "字1"]));

    // ---------- IsCjkToken ----------

    [Theory]
    [InlineData("你好", true)]
    [InlineData("世界。", true)]
    [InlineData("字0", true)]
    [InlineData("こんにちは", true)]
    [InlineData("hello", false)]
    [InlineData("안녕하세요", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsCjkToken_ClassifiesCorrectly(string? token, bool expected)
        => Assert.Equal(expected, LanguageSupport.IsCjkToken(token));

    // ---------- SplitPlainWords (mixed) ----------

    [Fact]
    public void SplitPlainWords_MixedSentence_TimesAreAllocatedPerToken()
    {
        var words = SubtitleWordSplitter.SplitPlainWords("hello 世界 world", 0, 3000, "en");
        Assert.Equal(4, words.Count);
        Assert.Equal(new[] { "hello", "世", "界", "world" }, words.Select(w => w.Text));
        Assert.Equal(0, words[0].Start);
        Assert.Equal(3000, words[^1].End, 1);
        Assert.Equal("hello 世界 world",
            LanguageSupport.JoinMixed(words.Select(w => w.Text)));
    }

    // ---------- Karaoke (mixed translation) ----------

    [Fact]
    public void TranslationKaraoke_MixedText_KeepsTokenCount()
    {
        var result = TranslationKaraokeBuilder.Build("hello 世界 world", 0, 3000, "zh");
        // 4 tokens → 4 个 \K 标签（含行首 lead）+ 无多余空格错乱
        Assert.Equal(5, result.Split("{\\K", StringSplitOptions.None).Length - 1);
        Assert.Contains("hello", result);
        Assert.Contains("世", result);
        Assert.Contains("界", result);
        Assert.Contains("world", result);
    }

    [Fact]
    public void TranslationKaraoke_PureChinese_OneTagPerChar()
    {
        var result = TranslationKaraokeBuilder.Build("你好世界", 0, 2000, "zh");
        Assert.Equal(5, result.Split("{\\K", StringSplitOptions.None).Length - 1); // lead + 4 chars
        Assert.DoesNotContain(" ", result);
    }

    // ---------- Rule-based splitting (mixed) ----------

    [Fact]
    public async Task RuleBasedSplit_MixedEnglishChinese_PreservesTightness()
    {
        // 中英混写：中文段直连，中文↔英文之间保留原文空格；长度约束按显示长度
        var words = new List<Word>
        {
            new() { Text = "hello", Start = 0, End = 500, Speaker = string.Empty },
            new() { Text = "世界", Start = 500, End = 1000, Speaker = string.Empty },
            new() { Text = "world。", Start = 1000, End = 1500, Speaker = string.Empty },
            new() { Text = "今天", Start = 1600, End = 2100, Speaker = string.Empty },
            new() { Text = "天气", Start = 2100, End = 2500, Speaker = string.Empty },
            new() { Text = "真好！", Start = 2500, End = 3000, Speaker = string.Empty }
        };

        var strategy = new AggressiveRuleSplitStrategy();
        var sentences = await strategy.Split(words, new SplitOptions { MaxLength = 20, TargetLength = 15, Language = "en" });

        Assert.Equal(2, sentences.Count);
        Assert.Equal("hello 世界 world。", sentences[0].Text);   // 中英混合：中文段直连、词间单空格
        Assert.Equal("今天天气真好！", sentences[1].Text);       // 纯中文：无空格
    }

    [Fact]
    public async Task RuleBasedSplit_Passive_MixedText_RespectsMaxLength()
    {
        // 消极档同样遵守混合长度约束：类 CJK 词直连不超长
        var words = new List<Word>();
        for (var i = 0; i < 8; i++)
        {
            words.Add(new Word { Text = $"字{i}", Start = i * 100, End = (i + 1) * 100, Speaker = string.Empty });
            words.Add(new Word { Text = "tok", Start = i * 100 + 50, End = (i + 1) * 100, Speaker = string.Empty });
        }

        var strategy = new PassiveRuleSplitStrategy();
        var sentences = await strategy.Split(words, new SplitOptions { MaxLength = 12, TargetLength = 8, Language = "en" });

        Assert.True(sentences.Count > 1);
        Assert.All(sentences, s => Assert.True(s.Text.Length <= 12));
        Assert.All(sentences, s => Assert.Contains("字", s.Text));
    }
}
