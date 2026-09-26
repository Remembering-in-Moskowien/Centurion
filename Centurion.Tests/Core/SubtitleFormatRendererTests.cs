using Centurion.Models;
using Centurion.Models.Workflow;
using Xunit;
using Centurion.Core.Utils.Reporting;
namespace Centurion.Tests.Core;

/// <summary>
/// build 命令的 SRT / TXT 渲染测试：时间轴格式、双语拼接、说话人前缀、跳过规则与标签清理。
/// </summary>
public sealed class SubtitleFormatRendererTests
{
    private static SubtitleWorkflowContext CreateContext(bool bilingual = true, bool showSpeakers = true)
    {
        var config = new WorkflowConfig
        {
            CommandName = "spawn",
            Language = "en",
            Bilingual = bilingual,
            ShowSpeakerLabels = showSpeakers
        };
        var context = new SubtitleWorkflowContext(config);
        context.State.IsDiarized = true;
        context.State.CurrentSentences =
        [
            new Sentence
            {
                Text = "Hello world.",
                TranslatedText = "你好，世界。",
                Start = 93_650,
                End = 99_220,
                Words = [new Word { Text = "Hello", Start = 93_650, End = 95_000, Speaker = "speaker 0" }]
            },
            new Sentence
            {
                Text = "One more line.",
                Start = 101_540,
                End = 103_520,
                Words = [new Word { Text = "One", Start = 101_540, End = 102_000, Speaker = "speaker 1" }]
            },
            new Sentence
            {
                Text = "Skipped line.",
                Start = 0,
                End = 0,
                SkipRender = true
            }
        ];
        return context;
    }

    [Fact]
    public void RenderSrt_ProducesTimelineAndNumbering()
    {
        var srt = SubtitleFormatRenderer.RenderSrt(CreateContext());

        Assert.StartsWith("1\n", srt);
        Assert.Contains("00:01:33,650 --> 00:01:39,220\n", srt);
        Assert.Contains("2\n", srt);
        Assert.Contains("00:01:41,540 --> 00:01:43,520\n", srt);
        // 被跳过的句子不渲染
        Assert.DoesNotContain("Skipped line.", srt);
    }

    [Fact]
    public void RenderSrt_Bilingual_JoinsSourceAndTranslation()
    {
        var srt = SubtitleFormatRenderer.RenderSrt(CreateContext(bilingual: true));

        Assert.Contains("Hello world.\n你好，世界。", srt);
    }

    [Fact]
    public void RenderSrt_SingleLanguage_UsesTranslationOnly()
    {
        var srt = SubtitleFormatRenderer.RenderSrt(CreateContext(bilingual: false));

        Assert.Contains("你好，世界。", srt);
        Assert.DoesNotContain("Hello world.\n你好", srt);
        Assert.Contains("One more line.", srt);
    }

    [Fact]
    public void RenderSrt_NoSpeakerLabels_OmitsPrefix()
    {
        var srt = SubtitleFormatRenderer.RenderSrt(CreateContext(showSpeakers: false));

        Assert.DoesNotContain("[speaker", srt);
        Assert.Contains("Hello world.", srt);
    }

    [Fact]
    public void RenderSrt_WithSpeakerLabels_PrefixesText()
    {
        var srt = SubtitleFormatRenderer.RenderSrt(CreateContext(showSpeakers: true));

        Assert.Contains("[speaker 0] Hello world.\n你好，世界。", srt);
        Assert.Contains("[speaker 1] One more line.", srt);
    }

    [Fact]
    public void RenderSrt_StripsAssOverrideTags()
    {
        var config = new WorkflowConfig { CommandName = "spawn", Bilingual = false };
        var context = new SubtitleWorkflowContext(config);
        context.State.CurrentSentences =
        [
            new Sentence
            {
                Text = "{\\b1}Bold{\\b0} and \\Kkaraoke.",
                Start = 1000,
                End = 2000
            }
        ];

        var srt = SubtitleFormatRenderer.RenderSrt(context);

        Assert.Contains("Bold and karaoke.", srt);
        Assert.DoesNotContain("{\\", srt);
        Assert.DoesNotContain("\\K", srt);
    }

    [Fact]
    public void RenderTxt_OneLinePerSentence()
    {
        var txt = SubtitleFormatRenderer.RenderTxt(CreateContext(bilingual: false));

        var lines = txt.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Contains(lines, line => line.Contains("你好，世界。"));
        Assert.Contains(lines, line => line.Contains("One more line."));
        Assert.DoesNotContain("Skipped line.", txt);
    }

    [Fact]
    public void RenderTxt_ZeroLengthSentence_GetsMinimumDuration()
    {
        var config = new WorkflowConfig { CommandName = "spawn" };
        var context = new SubtitleWorkflowContext(config);
        context.State.CurrentSentences =
        [
            new Sentence { Text = "Instant.", Start = 5000, End = 5000 }
        ];

        var srt = SubtitleFormatRenderer.RenderSrt(context);

        Assert.Contains("00:00:05,000 --> 00:00:05,080\nInstant.", srt);
    }
}
