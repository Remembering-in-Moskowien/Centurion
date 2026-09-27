using Centurion.Models;
using Xunit;
using Centurion.Core.Utils.Parsing;
namespace Centurion.Tests.Core;

public sealed class OverlapResolverTests
{
    private static Sentence Make(string text, double start, double end, params (string Word, double S, double E)[] words)
    {
        return new Sentence
        {
            Text = text,
            Start = start,
            End = end,
            Words = words
                .Select(w => new Word { Text = w.Word, Start = w.S, End = w.E, Speaker = string.Empty })
                .ToList()
        };
    }

    [Fact]
    public void Resolve_NoOverlap_KeepsTimingsUntouched()
    {
        List<Sentence> sentences =
        [
            Make("one", 0, 2),
            Make("two", 3, 5)
        ];

        var changed = TimelineOverlapResolver.Resolve(sentences);

        Assert.Equal(0, changed);
        Assert.Equal(0, sentences[0].Start);
        Assert.Equal(2, sentences[0].End);
        Assert.Equal(3, sentences[1].Start);
    }

    [Fact]
    public void Resolve_WordLevelNonOverlap_TrimsPreviousWindowToNextWordStart()
    {
        // 前句窗口 0-8，但内容止于 5；后句内容始于 6 → 窗口重叠、内容不重叠
        List<Sentence> sentences =
        [
            Make("hello world", 0, 8, ("hello", 0, 2), ("world", 2, 5)),
            Make("goodbye", 6, 9, ("goodbye", 6, 9))
        ];

        var changed = TimelineOverlapResolver.Resolve(sentences);

        Assert.True(changed > 0);
        Assert.Equal(6, sentences[0].End);      // 收窄到后句首词开始
        Assert.Equal(6, sentences[1].Start);    // 后句不动
    }

    [Fact]
    public void Resolve_ContentOverlap_SplitsOverlapEqually()
    {
        List<Sentence> sentences =
        [
            Make("aaaa", 0, 10, ("aaaa", 0, 10)),
            Make("bbbb", 8, 18, ("bbbb", 8, 18))
        ];

        var changed = TimelineOverlapResolver.Resolve(sentences);

        Assert.True(changed > 0);
        Assert.Equal(9, sentences[0].End, 3);      // 10 - 2/2
        Assert.Equal(9, sentences[1].Start, 3);    // 8 + 2/2
        Assert.True(sentences[1].Start >= sentences[0].End - 1e-6);
    }

    [Fact]
    public void Resolve_UnsortedInput_OrdersByStartTime()
    {
        List<Sentence> sentences =
        [
            Make("later", 20, 25),
            Make("earlier", 5, 8)
        ];

        TimelineOverlapResolver.Resolve(sentences);

        Assert.Equal(2, sentences.Count);
        Assert.Equal("earlier", sentences[0].Text);
        Assert.Equal("later", sentences[1].Text);
    }

    [Fact]
    public void Resolve_ChainOverlap_ProducesMonotonicTimeline()
    {
        List<Sentence> sentences =
        [
            Make("one", 0, 8, ("one", 0, 8)),
            Make("two", 6, 14, ("two", 6, 14)),
            Make("three", 12, 20, ("three", 12, 20))
        ];

        TimelineOverlapResolver.Resolve(sentences);

        for (var i = 1; i < sentences.Count; i++)
            Assert.True(sentences[i].Start >= sentences[i - 1].End - 1e-6,
                $"pair {i - 1}->{i} still overlapping");
    }
}
