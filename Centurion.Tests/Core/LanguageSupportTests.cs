using Centurion.Abstractions.Strategy;
using Centurion.Core.Workflow.Strategy.SentenceSplit;using Centurion.Models;
using Centurion.Models.Text;
using Xunit;

namespace Centurion.Tests.Core;

public sealed class LanguageSupportTests
{
    [Theory]
    [InlineData("zh", true)]
    [InlineData("zh-cn", true)]
    [InlineData("zh-TW", true)]
    [InlineData("ja", true)]
    [InlineData("ja-jp", true)]
    [InlineData("ko", false)]      // 韩语谚文用空格分隔词
    [InlineData("ko-kr", false)]
    [InlineData("en", false)]
    [InlineData("fr", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsSpaceless_DetectsCjkLanguages(string? lang, bool expected)
        => Assert.Equal(expected, LanguageSupport.IsSpaceless(lang));

    [Fact]
    public void JoinWords_Cjk_ConcatenatesWithoutSpaces()
        => Assert.Equal("我爱北京天安门", LanguageSupport.JoinWords(["我", "爱", "北京", "天安门"], "zh"));

    [Fact]
    public void JoinWords_Japanese_ConcatenatesWithoutSpaces()
        => Assert.Equal("こんにちは世界", LanguageSupport.JoinWords(["こんにちは", "世界"], "ja"));

    [Fact]
    public void JoinWords_Korean_KeepsSpaces()
        => Assert.Equal("안녕하세요 세계", LanguageSupport.JoinWords(["안녕하세요", "세계"], "ko"));

    [Fact]
    public void JoinWords_Latin_JoinsWithSpaces()
        => Assert.Equal("I love Beijing", LanguageSupport.JoinWords(["I", "love", "Beijing"], "en"));

    [Fact]
    public void JoinWords_NullLanguage_FallsBackToSpaces()
        => Assert.Equal("a b", LanguageSupport.JoinWords(["a", "b"], null));
}

public sealed class ChineseSplitTests
{
    private static SplitOptions Options(int maxLength, string language = "zh") => new()
    {
        MaxLength = maxLength,
        TargetLength = 30,
        Language = language
    };

    [Fact]
    public async Task RuleBasedSplit_ChinesePunctuation_BreaksAtFullStop()
    {
        // 总字符数超出 MaxLength 硬约束，必须断句；断点应优先落在中文句号后
        var words = new List<Word>
        {
            new() { Text = "你好", Start = 0, End = 500, Speaker = string.Empty },
            new() { Text = "世界。", Start = 500, End = 1000, Speaker = string.Empty },
            new() { Text = "今天", Start = 1100, End = 1600, Speaker = string.Empty },
            new() { Text = "天气", Start = 1600, End = 2000, Speaker = string.Empty },
            new() { Text = "真好！", Start = 2000, End = 2500, Speaker = string.Empty }
        };

        var strategy = new AggressiveRuleSplitStrategy();
        var sentences = await strategy.Split(words, Options(maxLength: 8));

        Assert.Equal(2, sentences.Count);
        Assert.Equal("你好世界。", sentences[0].Text);      // 断在中文句号后，且无空格拼接
        Assert.Equal("今天天气真好！", sentences[1].Text);
        Assert.DoesNotContain(" ", sentences[0].Text);
        Assert.DoesNotContain(" ", sentences[1].Text);
    }

    [Fact]
    public async Task RuleBasedSplit_Japanese_NoSpacesAndBreaksAtJapanesePunctuation()
    {
        // 日语连续书写无空格；句号（。）触发分句
        var words = new List<Word>
        {
            new() { Text = "こんにちは", Start = 0, End = 500, Speaker = string.Empty },
            new() { Text = "世界。", Start = 500, End = 1000, Speaker = string.Empty },
            new() { Text = "また", Start = 1100, End = 1600, Speaker = string.Empty },
            new() { Text = "明日", Start = 1600, End = 2000, Speaker = string.Empty },
            new() { Text = "会いましょう！", Start = 2000, End = 2500, Speaker = string.Empty }
        };

        var strategy = new AggressiveRuleSplitStrategy();
        var sentences = await strategy.Split(words, Options(maxLength: 12, language: "ja"));

        Assert.Equal(2, sentences.Count);
        Assert.Equal("こんにちは世界。", sentences[0].Text);
        Assert.Equal("また明日会いましょう！", sentences[1].Text);
        Assert.All(sentences, s => Assert.DoesNotContain(" ", s.Text));
    }

    [Fact]
    public async Task RuleBasedSplit_ChineseNoPunctuation_SplitsByLengthWithoutSpaces()
    {
        // 无标点长文本：MaxLength 硬约束强制按长度断开，输出仍无空格且不超长
        var words = Enumerable.Range(0, 20)
            .Select(i => new Word
            {
                Text = $"字{i}",
                Start = i * 100,
                End = (i + 1) * 100,
                Speaker = string.Empty
            })
            .ToList();

        var strategy = new AggressiveRuleSplitStrategy();
        var sentences = await strategy.Split(words, Options(maxLength: 15));

        Assert.True(sentences.Count > 1);
        Assert.All(sentences, s => Assert.DoesNotContain(" ", s.Text));
        Assert.All(sentences, s => Assert.True(s.Text.Length <= 15));
    }
}
