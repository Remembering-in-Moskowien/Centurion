using Centurion.Core.Text;
using Xunit;

namespace Centurion.Tests;

public sealed class TextToolsTests
{
    [Fact]
    public void Similarity_HandlesEmptyAndEqualStrings()
    {
        Assert.Equal(1, TextSimilarity.Similarity(string.Empty, string.Empty));
        Assert.Equal(0, TextSimilarity.Similarity(string.Empty, "text"));
        Assert.Equal(1, TextSimilarity.Similarity("same", "same"));
    }

    [Fact]
    public void Similarity_ReturnsHalfForSingleEditInTwoCharacters()
    {
        Assert.Equal(0.5, TextSimilarity.Similarity("of", "or"));
    }

    [Fact]
    public void Tokenizer_SplitsCjkAndLatinText()
    {
        var tokens = Tokenizer.Tokenize("你好 world test");

        Assert.Equal(["你", "好", "world", "test"], tokens);
    }

    [Fact]
    public void Tokenizer_NormalizesPunctuationAndFullWidthCharacters()
    {
        var tokens = Tokenizer.Tokenize("ＡＢＣ，测试！");

        Assert.Equal(["abc", "测", "试"], tokens);
    }
}
