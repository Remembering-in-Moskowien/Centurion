using Centurion.Abstractions.Strategy;
using Centurion.Core.Workflow.Strategy.SentenceSplit;using Centurion.Models;
using Centurion.Models.Ass;
using Centurion.Models.Text;
using Xunit;
using Centurion.Core.Utils.Parsing;
namespace Centurion.Tests.Core;

/// <summary>
/// Tests for mixed-language text (code-switching): when English listening material contains Chinese or Japanese,
/// covering mix-aware tokenization, joining, sentence splitting, and karaoke timestamp building.
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
        // 4 tokens → 4 \K tags (including the leading lead), with no stray-space corruption.
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
        // Mixed Chinese-English writing: CJK runs join directly, original spaces are kept between Chinese and English; length limits use display length.
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
        Assert.Equal("hello 世界 world。", sentences[0].Text);   // mixed Chinese-English: CJK runs joined directly, single space between words
        Assert.Equal("今天天气真好！", sentences[1].Text);       // pure Chinese: no spaces
    }

    [Fact]
    public async Task RuleBasedSplit_Passive_MixedText_RespectsMaxLength()
    {
        // The passive profile also honors mixed-length limits: CJK-like words join directly without exceeding the limit.
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
