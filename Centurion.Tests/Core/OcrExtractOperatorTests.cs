using Centurion.Core.Pipeline.Operators;
using Centurion.Core.Utils;
using Centurion.Models;
using Xunit;

namespace Centurion.Tests.Core;

/// <summary>OCR 提取与共享分词的纯逻辑测试（不依赖外部 API）。</summary>
public sealed class OcrExtractOperatorTests
{
    [Theory]
    [InlineData("   ", "")]
    [InlineData("[NO_TEXT]", "")]
    [InlineData("Hello\nWorld\n", "Hello\nWorld")]
    [InlineData("  Subtitle one  \n[NO_TEXT]\n  Subtitle two  ", "Subtitle one\nSubtitle two")]
    public void NormalizeText_StripsNoiseAndBlankLines(string raw, string expected)
    {
        var result = OcrExtractOperator.NormalizeText(raw);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void MergeSegments_ConsecutiveIdenticalText_MergesTiming()
    {
        var segments = new List<OcrExtractOperator.OcrSegment>
        {
            new("Hello world", 0, 2000),
            new("Hello world", 2000, 4000),
            new("Different line", 4000, 6000)
        };

        var sentences = OcrExtractOperator.MergeSegments(segments, "en");

        Assert.Equal(2, sentences.Count);
        Assert.Equal("Hello world", sentences[0].Text);
        Assert.Equal(0, sentences[0].Start);
        Assert.Equal(4000, sentences[0].End);
        Assert.Equal("Different line", sentences[1].Text);
        Assert.Equal(4000, sentences[1].Start);
        Assert.Equal(6000, sentences[1].End);
    }

    [Fact]
    public void MergeSegments_NoSegments_ReturnsEmpty()
    {
        Assert.Empty(OcrExtractOperator.MergeSegments([], "en"));
    }

    [Fact]
    public void ToSentence_SplitsWordsByLanguage()
    {
        var sentence = OcrExtractOperator.ToSentence(
            new OcrExtractOperator.OcrSegment("Hello brave world", 0, 3000), "en");

        Assert.Equal(3, sentence.Words.Count);
        Assert.Equal("Hello", sentence.Words[0].Text);
        Assert.Equal(3000, sentence.End);
    }

    [Fact]
    public void SplitPlainWords_Cjk_OneCharPerWord()
    {
        var words = SubtitleWordSplitter.SplitPlainWords("你好世界", 0, 4000, "zh");

        Assert.Equal(4, words.Count);
        Assert.Equal("你", words[0].Text);
        Assert.Equal(1000, words[1].Start);
        Assert.Equal(4000, words[^1].End);
    }

    [Fact]
    public void SplitPlainWords_WhitespaceLanguages_SplitsOnSpaces()
    {
        var words = SubtitleWordSplitter.SplitPlainWords("the quick fox", 0, 3000, "en");

        Assert.Equal(3, words.Count);
        Assert.Equal("quick", words[1].Text);
        Assert.Equal(1000, words[1].Start);
    }

    [Fact]
    public void SplitPlainWords_EmptyText_ReturnsEmpty()
    {
        Assert.Empty(SubtitleWordSplitter.SplitPlainWords("   ", 0, 1000, "en"));
    }

    [Fact]
    public void SplitPlainWords_ZeroDuration_SingleTimestamp()
    {
        var words = SubtitleWordSplitter.SplitPlainWords("ab cd", 5000, 5000, "en");

        Assert.All(words, w => Assert.Equal(5000, w.Start));
        Assert.All(words, w => Assert.Equal(5000, w.End));
    }
}
