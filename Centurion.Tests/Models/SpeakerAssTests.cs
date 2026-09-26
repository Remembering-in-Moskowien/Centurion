using Centurion.Models;
using Centurion.Models.Ass;
using Centurion.Models.Workflow;
using Xunit;

namespace Centurion.Tests.Models;

public sealed class SpeakerAssTests
{
    private static SubtitleWorkflowContext BuildContext(
        List<Sentence> sentences, bool isDiarized, bool showLabels = true)
    {
        var config = new WorkflowConfig
        {
            InputFilePath = @"C:\x\movie.mp4",
            ShowSpeakerLabels = showLabels
        };
        var context = new SubtitleWorkflowContext(config);
        context.State.CorrectedSentences = sentences;
        context.State.IsDiarized = isDiarized;
        return context;
    }

    private static Sentence BuildSentence(string text, double start, double end, params (string Word, string Speaker)[] words) => new()
    {
        Text = text,
        Start = start,
        End = end,
        Words = words.Select(w => new Word { Text = w.Word, Start = start, End = end, Speaker = w.Speaker }).ToList()
    };

    // ---------- Sentence.Speaker 推导 ----------

    [Fact]
    public void SentenceSpeaker_MajorityVote_Wins()
    {
        var sentence = BuildSentence("Hello.", 0, 1000,
            ("Hello", "SPEAKER_01"), ("world", "SPEAKER_01"), ("bye", "SPEAKER_02"));

        Assert.Equal("SPEAKER_01", sentence.Speaker);
    }

    [Fact]
    public void SentenceSpeaker_NoWords_ReturnsNull()
    {
        Assert.Null(new Sentence { Text = "Hi", Start = 0, End = 1 }.Speaker);
    }

    // ---------- ASS 输出 ----------

    [Fact]
    public void AssOutput_NotDiarized_WritesNoSpeaker()
    {
        var context = BuildContext(
        [
            BuildSentence("Hello world.", 0, 2000, ("Hello", "SPEAKER_00"), ("world", "SPEAKER_00"))
        ], isDiarized: false);

        var ass = AssSubBuilder.FromWorkflow(context).Build().ToString();
        var dialogue = ass.Split('\n').First(line => line.StartsWith("Dialogue:"));

        // 第 5 个字段（Name）为空，且文本无 [SPEAKER_00] 前缀
        Assert.Equal(string.Empty, dialogue.Split(',')[4].Trim());
        Assert.DoesNotContain("[SPEAKER_00]", dialogue);
        Assert.Contains("Hello world", dialogue);
    }

    [Fact]
    public void AssOutput_Diarized_WritesNameAndLabelPrefix()
    {
        var context = BuildContext(
        [
            BuildSentence("Hello world.", 0, 2000, ("Hello", "SPEAKER_01"), ("world", "SPEAKER_01"))
        ], isDiarized: true);

        var ass = AssSubBuilder.FromWorkflow(context).Build().ToString();
        var dialogue = ass.Split('\n').First(line => line.StartsWith("Dialogue:"));

        // Name 字段 = SPEAKER_01；文本带 [SPEAKER_01] 前缀
        Assert.Equal("SPEAKER_01", dialogue.Split(',')[4].Trim());
        Assert.Contains("[SPEAKER_01] Hello world", dialogue);
    }

    [Fact]
    public void AssOutput_Diarized_ShowLabelsOff_KeepsNameOnly()
    {
        var context = BuildContext(
        [
            BuildSentence("Hello world.", 0, 2000, ("Hello", "SPEAKER_02"), ("world", "SPEAKER_02"))
        ], isDiarized: true, showLabels: false);

        var ass = AssSubBuilder.FromWorkflow(context).Build().ToString();
        var dialogue = ass.Split('\n').First(line => line.StartsWith("Dialogue:"));

        Assert.Equal("SPEAKER_02", dialogue.Split(',')[4].Trim());
        Assert.DoesNotContain("[SPEAKER_02]", dialogue);
        Assert.Contains("Hello world", dialogue);
    }

    [Fact]
    public void AssOutput_Bilingual_PrefixOnlyOnMainLine_NamesOnBoth()
    {
        var config = new WorkflowConfig
        {
            InputFilePath = @"C:\x\movie.mp4",
            TargetLanguage = "zh",
            Bilingual = true,
            ShowSpeakerLabels = true
        };
        var context = new SubtitleWorkflowContext(config);
        context.State.IsDiarized = true;
        context.State.CorrectedSentences =
        [
            new Sentence
            {
                Text = "Hello world.",
                Start = 0,
                End = 2000,
                TranslatedText = "你好世界。",
                Words =
                [
                    new Word { Text = "Hello", Start = 0, End = 1000, Speaker = "SPEAKER_03" },
                    new Word { Text = "world", Start = 1000, End = 2000, Speaker = "SPEAKER_03" }
                ]
            }
        ];

        var ass = AssSubBuilder.FromWorkflow(context).Build().ToString();
        var dialogues = ass.Split('\n').Where(line => line.StartsWith("Dialogue:")).ToList();

        var main = dialogues.Single(line => line.Contains(",Default,"));
        var sub = dialogues.Single(line => line.Contains(",Sub,"));

        Assert.Equal("SPEAKER_03", main.Split(',')[4].Trim());
        Assert.Equal("SPEAKER_03", sub.Split(',')[4].Trim());
        Assert.Contains("[SPEAKER_03] Hello world", main);
        Assert.DoesNotContain("[SPEAKER_03]", sub);
        Assert.Contains("你好世界。", sub);
    }
}
