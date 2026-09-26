using Centurion.Core.Capabilities.Infrastructure.Ocr;
using Centurion.Core.Workflow.Pipeline.Operators;using Centurion.Models;
using Xunit;
using Centurion.Core.Utils.Parsing;
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
    public void CreateVideoSubFinderFrames_PairsImagesWithSrtTimes()
    {
        var frames = OcrExtractOperator.CreateVideoSubFinderFrames(
            ["0_00_00_004__0_00_00_005_0070000000019200080001920.jpeg",
             "0_00_00_001__0_00_00_002_0070000000019200080001920.jpeg"],
            "1\r\n00:00:00,001 --> 00:00:00,002\r\n\r\n2\r\n00:00:00,004 --> 00:00:00,005\r\n\r\n");

        Assert.Collection(frames,
            frame =>
            {
                Assert.Equal("0_00_00_001__0_00_00_002_0070000000019200080001920.jpeg", frame.Path);
                Assert.Equal(1, frame.StartMs);
                Assert.Equal(2, frame.EndMs);
            },
            frame =>
            {
                Assert.Equal("0_00_00_004__0_00_00_005_0070000000019200080001920.jpeg", frame.Path);
                Assert.Equal(4, frame.StartMs);
                Assert.Equal(5, frame.EndMs);
            });
    }

    [Fact]
    public void CreateVideoSubFinderFrames_SortsByNumericTime_NotLexicographic()
    {
        // 10 小时的字符串序在 2 小时之前（"10_..." < "2_..."），必须按数值排序
        var frames = OcrExtractOperator.CreateVideoSubFinderFrames(
            ["10_00_00_000__10_00_00_500_0070000000019200080001920.jpeg",
             "2_00_00_000__2_00_00_500_0070000000019200080001920.jpeg"],
            "1\r\n02:00:00,000 --> 02:00:00,500\r\n\r\n2\r\n10:00:00,000 --> 10:00:00,500\r\n\r\n");

        Assert.Collection(frames,
            frame =>
            {
                Assert.Equal("2_00_00_000__2_00_00_500_0070000000019200080001920.jpeg", frame.Path);
                Assert.Equal(2 * 3600_000, frame.StartMs);
            },
            frame =>
            {
                Assert.Equal("10_00_00_000__10_00_00_500_0070000000019200080001920.jpeg", frame.Path);
                Assert.Equal(10 * 3600_000, frame.StartMs);
            });
    }

    [Fact]
    public void CreateVideoSubFinderFrames_TimingMismatch_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => OcrExtractOperator.CreateVideoSubFinderFrames(
            ["0_00_00_001__0_00_00_002_0070000000019200080001920.jpeg"],
            "1\n00:00:01,200 --> 00:00:02,300\n"));
    }

    [Fact]
    public void CreateVideoSubFinderFrames_UnrecognizedImageName_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => OcrExtractOperator.CreateVideoSubFinderFrames(
            ["frame_1.bmp"],
            "1\n00:00:01,200 --> 00:00:02,300\n"));
    }

    [Fact]
    public void CreateVideoSubFinderFrames_MismatchedOutputCounts_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => OcrExtractOperator.CreateVideoSubFinderFrames(
            ["0_00_00_001__0_00_00_002_0070000000019200080001920.jpeg"],
            "1\n00:00:01,200 --> 00:00:02,300\n\n2\n00:00:04,000 --> 00:00:05,500\n"));
    }

    [Fact]
    public void LocateVideoSubFinderExecutable_FindsDefaultOnPath()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var executable = Path.Combine(directory, "VideoSubFinderCli.exe");
            File.WriteAllBytes(executable, []);

            Assert.Equal(executable, OcrExtractOperator.LocateVideoSubFinderExecutable(
                null, [directory], isWindows: true));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void LocateVideoSubFinderExecutable_ExplicitPathTakesPrecedence()
    {
        Assert.Equal("custom-vsf.exe", OcrExtractOperator.LocateVideoSubFinderExecutable(
            "custom-vsf.exe", [], isWindows: true));
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

    [Theory]
    [InlineData("rapidocr", OcrBackend.RapidOcr)]
    [InlineData("rapid-ocr", OcrBackend.RapidOcr)]
    [InlineData("rapid", OcrBackend.RapidOcr)]
    [InlineData("RAPIDOCR", OcrBackend.RapidOcr)]
    [InlineData("zhipu", OcrBackend.Zhipu)]
    [InlineData("ollama", OcrBackend.Ollama)]
    [InlineData("llamacpp", OcrBackend.LlamaCpp)]
    public void ParseBackend_RapidOcrAndExistingAliases(string value, OcrBackend expected)
    {
        Assert.Equal(expected, OcrExtractOperator.ParseBackend(value));
    }
}
