using Centurion.Core.Workflow.Pipeline.Operators;using Centurion.Models;
using Xunit;

namespace Centurion.Tests.Core;

/// <summary>
/// dub（媒体译制）相关单元测试：双语字幕解析、译文时间窗匹配。
/// </summary>
public class DubOperatorTests
{
    [Fact]
    public void MatchTranslations_PairsByClosestCenter()
    {
        var source = new List<Sentence>
        {
            new() { Text = "Hello world.", Start = 1000, End = 3000 },
            new() { Text = "How are you?", Start = 3500, End = 5000 }
        };
        var translations = new List<Sentence>
        {
            new() { Text = "你好世界。", Start = 1200, End = 3100 },
            new() { Text = "你好吗？", Start = 3600, End = 4800 }
        };

        BilingualSubtitleParserOperator.MatchTranslations(source, translations);

        Assert.Equal("你好世界。", source[0].TranslatedText);
        Assert.Equal("你好吗？", source[1].TranslatedText);
    }

    [Fact]
    public void MatchTranslations_OutOfWindow_LeavesNull()
    {
        var source = new List<Sentence> { new() { Text = "Hello.", Start = 0, End = 500 } };
        var translations = new List<Sentence> { new() { Text = "太远了", Start = 50000, End = 52000 } };

        BilingualSubtitleParserOperator.MatchTranslations(source, translations);

        Assert.Null(source[0].TranslatedText);
    }

    [Fact]
    public async Task ParseAsync_ReadsSrtTimingsInMilliseconds()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dub_test_{Guid.NewGuid():N}.srt");
        File.WriteAllText(path, "1\n00:00:01,000 --> 00:00:02,500\nHello there.\n", new System.Text.UTF8Encoding(true));

        try
        {
            var sentences = await BilingualSubtitleParserOperator.ParseAsync(path, CancellationToken.None);
            var sentence = Assert.Single(sentences);
            Assert.Equal("Hello there.", sentence.Text);
            Assert.Equal(1000, sentence.Start);
            Assert.Equal(2500, sentence.End);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void DubSegment_TargetDuration_ComputesFromMs()
    {
        var segment = new Centurion.Models.Workflow.DubSegment { TargetStartMs = 2000, TargetEndMs = 5000 };
        Assert.Equal(3.0, segment.TargetDurationSec);
    }
}
