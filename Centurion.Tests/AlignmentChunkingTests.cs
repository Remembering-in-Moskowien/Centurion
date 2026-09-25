using Centurion.Core.Strategy.Alignment;
using Centurion.Models;
using Xunit;

namespace Centurion.Tests;

public sealed class AlignmentChunkingTests
{
    private static Sentence MakeSentence(double start, double end, params string[] words) => new()
    {
        Text = string.Join(" ", words),
        Start = start,
        End = end,
        Words = words.Select((w, i) => new Word
        {
            Text = w,
            Start = start,
            End = end,
            Speaker = "UNKNOWN",
            Status = MappingStatus.Matched
        }).ToList()
    };

    // ---------- BuildChunks ----------

    [Fact]
    public void BuildChunks_NoGap_ProducesSingleChunk()
    {
        List<Sentence> sentences =
        [
            MakeSentence(0, 2000, "Hello", "world"),
            MakeSentence(2100, 4000, "This", "is", "next")
        ];

        var chunks = CrispAsrAlignmentStrategy.BuildChunks(sentences);

        Assert.Single(chunks);
        Assert.Equal(2, chunks[0].Sentences.Count);
        Assert.Equal(0, chunks[0].StartSeconds);
        Assert.Equal(4.0, chunks[0].EndSeconds);
    }

    [Fact]
    public void BuildChunks_GapExceeds_Splits()
    {
        List<Sentence> sentences =
        [
            MakeSentence(0, 2000, "Hello"),
            MakeSentence(5000, 7000, "World")
        ];

        var chunks = CrispAsrAlignmentStrategy.BuildChunks(sentences, chunkGapSeconds: 2.0);

        Assert.Equal(2, chunks.Count);
        Assert.Single(chunks[0].Sentences);
        Assert.Single(chunks[1].Sentences);
        Assert.Equal(5.0, chunks[1].StartSeconds);
    }

    [Fact]
    public void BuildChunks_MaxDuration_Splits()
    {
        List<Sentence> sentences =
        [
            MakeSentence(0, 100000, "Long"),
            MakeSentence(101000, 200000, "Chunk")
        ];

        var chunks = CrispAsrAlignmentStrategy.BuildChunks(sentences, maxChunkSeconds: 120.0, chunkGapSeconds: 100.0);

        Assert.Equal(2, chunks.Count);
    }

    [Fact]
    public void BuildChunks_MaxSentenceCount_Splits()
    {
        var sentences = Enumerable.Range(0, 10)
            .Select(i => MakeSentence(i * 1000.0, (i + 1) * 1000.0, "Word"))
            .ToList();

        var chunks = CrispAsrAlignmentStrategy.BuildChunks(sentences, maxSentencesPerChunk: 4);

        Assert.Equal(3, chunks.Count);
        Assert.All(chunks, chunk => Assert.True(chunk.Sentences.Count <= 4));
    }

    [Fact]
    public void BuildChunks_InvalidWindows_Excluded()
    {
        List<Sentence> sentences =
        [
            MakeSentence(0, 2000, "Valid"),
            new Sentence { Text = "Broken", Start = 3000, End = 3000 }
        ];

        var chunks = CrispAsrAlignmentStrategy.BuildChunks(sentences);

        Assert.Single(chunks);
        Assert.Single(chunks[0].Sentences);
    }

    // ---------- SplitTimingsAcrossSentences ----------

    [Fact]
    public void SplitTimings_ExactMatch_MapsByWordCount()
    {
        var sentence1 = MakeSentence(0, 2000, "Hello", "world");
        var sentence2 = MakeSentence(2000, 4000, "Nice", "to", "meet");
        var timings = new List<(double Start, double End)>
        {
            (0.1, 0.5), (0.6, 1.0),   // sentence1: 2 words
            (2.1, 2.4), (2.5, 2.8), (2.9, 3.2) // sentence2: 3 words
        };

        CrispAsrAlignmentStrategy.SplitTimingsAcrossSentences([sentence1, sentence2], timings, offsetMilliseconds: 1000);

        Assert.Equal(1100, sentence1.Words[0].Start);
        Assert.Equal(2000, sentence1.Words[1].End);
        Assert.Equal(3100, sentence2.Words[0].Start);
        Assert.Equal(4200, sentence2.Words[2].End);
        Assert.Equal(1100, sentence1.Start);
        Assert.Equal(4200, sentence2.End);
    }

    [Fact]
    public void SplitTimings_MoreTimingsThanWords_Truncates()
    {
        var sentence1 = MakeSentence(0, 2000, "Hello");
        var timings = new List<(double Start, double End)>
        {
            (0.1, 0.5), (0.6, 1.0), (1.1, 1.5)
        };

        CrispAsrAlignmentStrategy.SplitTimingsAcrossSentences([sentence1], timings, offsetMilliseconds: 0);

        // 仅第一个时间被消费（词数=1），其余截断
        Assert.Equal(100, sentence1.Words[0].Start);
        Assert.Equal(500, sentence1.Words[0].End);
    }

    [Fact]
    public void SplitTimings_FewerTimingsThanWords_KeepsCoarseForTail()
    {
        var sentence = MakeSentence(0, 4000, "One", "Two", "Three");
        var timings = new List<(double Start, double End)> { (0.1, 0.5) };

        CrispAsrAlignmentStrategy.SplitTimingsAcrossSentences([sentence], timings, offsetMilliseconds: 0);

        // 第一个词被细化，其余词保留粗时间（0→4000）
        Assert.Equal(100, sentence.Words[0].Start);
        Assert.Equal(500, sentence.Words[0].End);
        Assert.Equal(0, sentence.Words[1].Start);
        Assert.Equal(4000, sentence.Words[2].End);
    }

    [Fact]
    public void SplitTimings_NoWords_GeneratesFromText()
    {
        var sentence = new Sentence { Text = "Hello world", Start = 0, End = 2000, Words = [] };
        var timings = new List<(double Start, double End)> { (0.1, 0.5), (0.6, 1.0) };

        CrispAsrAlignmentStrategy.SplitTimingsAcrossSentences([sentence], timings, offsetMilliseconds: 500);

        Assert.Equal(2, sentence.Words.Count);
        Assert.Equal(600, sentence.Words[0].Start);
        Assert.Equal(1500, sentence.Words[1].End);
    }
}
