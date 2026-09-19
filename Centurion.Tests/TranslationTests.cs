using Centurion.Abstractions.Strategy;
using Centurion.Core.Strategy.Translation;
using Centurion.Core.Utils;
using Centurion.Models;
using Centurion.Models.Ass;
using Centurion.Models.Workflow;
using Xunit;

namespace Centurion.Tests;

public sealed class TranslationTests
{
    private static List<Sentence> MakeSentences(params string[] texts)
        => texts
            .Select((text, i) => new Sentence
            {
                Text = text,
                Start = i * 1000,
                End = (i + 1) * 1000,
                Words =
                [
                    new Word { Text = text, Start = i * 1000, End = (i + 1) * 1000, Speaker = string.Empty }
                ]
            })
            .ToList();

    // ---------- 目标台本 1:1 对齐 ----------

    [Fact]
    public void AlignToScript_MatchingLineCount_UsesScriptLinesDirectly()
    {
        var sentences = MakeSentences("Hello.", "Goodbye.");
        var script = new[] { "你好。", "再见。" };

        var aligned = LLMTranslationStrategy.AlignToScript(sentences, script);

        Assert.True(aligned);
        Assert.Equal("你好。", sentences[0].TranslatedText);
        Assert.Equal("再见。", sentences[1].TranslatedText);
    }

    [Fact]
    public void AlignToScript_MismatchedLineCount_ReturnsFalse()
    {
        var sentences = MakeSentences("Hello.", "Goodbye.", "Thanks.");
        var script = new[] { "你好。", "再见。" };

        var aligned = LLMTranslationStrategy.AlignToScript(sentences, script);

        Assert.False(aligned);
        Assert.All(sentences, s => Assert.Null(s.TranslatedText));
    }

    // ---------- LLM 响应解析 ----------

    [Fact]
    public void ParseResponse_ParsesJsonArray()
    {
        const string json = """[{"id":0,"translation":"你好。"},{"id":1,"translation":"再见。"}]""";

        var items = LLMTranslationStrategy.ParseResponse(json);

        Assert.Equal(2, items.Count);
        Assert.Equal(0, items[0].Id);
        Assert.Equal("你好。", items[0].Translation);
        Assert.Equal(1, items[1].Id);
    }

    [Fact]
    public void ParseResponse_ToleratesCodeFenceWrapper()
    {
        const string response = "```json\n[{\"id\":0,\"translation\":\"你好。\"}]\n```";

        var items = LLMTranslationStrategy.ParseResponse(response);

        Assert.Single(items);
        Assert.Equal("你好。", items[0].Translation);
    }

    // ---------- 提示词构建 ----------

    [Fact]
    public void BuildPrompt_IncludesGlossaryAndTargetScript()
    {
        var sentences = MakeSentences("Hello.");
        var options = new TranslationOptions
        {
            TargetLanguage = "zh",
            Glossary = new Dictionary<string, string> { ["Hello"] = "你好" },
            TargetScriptLines = new[] { "你好。" }
        };

        var prompt = LLMTranslationStrategy.BuildPrompt(sentences, options);

        Assert.Contains("zh", prompt);
        Assert.Contains("Hello -> 你好", prompt);
        Assert.Contains("你好。", prompt);
        Assert.Contains("\"id\":0", prompt);
    }

    // ---------- 术语表加载 ----------

    [Fact]
    public void GlossaryLoader_LoadsDictForm()
    {
        var path = Path.Combine(Path.GetTempPath(), $"glossary_{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{"Hello":"你好","world":"世界"}""");
        try
        {
            var glossary = GlossaryLoader.Load(path);

            Assert.Equal(2, glossary.Count);
            Assert.Equal("你好", glossary["Hello"]);
            Assert.Equal("世界", glossary["world"]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GlossaryLoader_LoadsArrayForm()
    {
        var path = Path.Combine(Path.GetTempPath(), $"glossary_{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """[{"source":"Hello","target":"你好"},{"source":"world","target":"世界"}]""");
        try
        {
            var glossary = GlossaryLoader.Load(path);

            Assert.Equal(2, glossary.Count);
            Assert.Equal("你好", glossary["Hello"]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ---------- ASS 输出（单语 / 双语） ----------

    [Fact]
    public void AssSubBuilder_TranslatedOutput_UsesTranslationOnly()
    {
        var config = new WorkflowConfig { InputFilePath = @"C:\x\movie.mp4", TargetLanguage = "zh" };
        var context = new SubtitleWorkflowContext(config);
        context.State.CorrectedSentences =
        [
            new Sentence
            {
                Text = "Hello world.",
                Start = 0,
                End = 2000,
                TranslatedText = "你好世界。",
                Words = []
            }
        ];

        var ass = AssSubBuilder.FromWorkflow(context).Build().ToString();
        var dialogue = ass.Split('\n').First(line => line.StartsWith("Dialogue:"));

        Assert.Contains("你好世界。", dialogue);
        Assert.DoesNotContain("Hello world.", dialogue);
    }

    [Fact]
    public void AssSubBuilder_BilingualOutput_EmitsTwoLinesWithMainAndSubStyles()
    {
        var config = new WorkflowConfig { InputFilePath = @"C:\x\movie.mp4", TargetLanguage = "zh", Bilingual = true };
        var context = new SubtitleWorkflowContext(config);
        context.State.CorrectedSentences =
        [
            new Sentence
            {
                Text = "Hello world.",
                Start = 0,
                End = 2000,
                TranslatedText = "你好世界。",
                Words = []
            }
        ];

        var ass = AssSubBuilder.FromWorkflow(context).Build().ToString();
        var dialogues = ass.Split('\n').Where(line => line.StartsWith("Dialogue:")).ToList();

        Assert.Equal(2, dialogues.Count);
        var main = dialogues.Single(line => line.Contains(",Default,"));
        var sub = dialogues.Single(line => line.Contains(",Sub,"));
        Assert.Contains("Hello world.", main);
        Assert.Contains("你好世界。", sub);
        Assert.DoesNotContain("\\N", dialogues[0]);
    }

    // ---------- 翻译词级时间戳（插值 + 长音节多分配） ----------

    [Fact]
    public void KaraokeBuilder_Chinese_TokenizesPerCharWithLead()
    {
        var result = TranslationKaraokeBuilder.Build("你好世界。", 1000, 6000, "zh");

        Assert.StartsWith("{\\K", result);
        // 标点按参考字幕风格过滤（Theme.ass 的 \K 行不含句号）
        Assert.Matches(@"\{\\K\d+\}你\{\\K\d+\}好\{\\K\d+\}世\{\\K\d+\}界", result);
    }

    [Fact]
    public void KaraokeBuilder_English_LongWordsGetMoreTime()
    {
        // "extraordinary"（多音节）应比 "a" 分配到更多 \K 时长
        var result = TranslationKaraokeBuilder.Build("a extraordinary", 0, 5000, "en");

        var aMatch = System.Text.RegularExpressions.Regex.Match(result, @"\{\\K(\d+)\}a");
        var exMatch = System.Text.RegularExpressions.Regex.Match(result, @"\{\\K(\d+)\}extraordinary");
        Assert.True(aMatch.Success && exMatch.Success);
        Assert.True(int.Parse(exMatch.Groups[1].Value) > int.Parse(aMatch.Groups[1].Value));
    }

    [Fact]
    public void KaraokeBuilder_TotalKValuesApproximateDuration()
    {
        var result = TranslationKaraokeBuilder.Build("你好世界。", 0, 5000, "zh");

        var sum = System.Text.RegularExpressions.Regex.Matches(result, @"\{\\K(\d+)\}")
            .Sum(m => int.Parse(m.Groups[1].Value));
        Assert.InRange(sum, 490, 520);   // 5000ms = 500cs，容差 ±20cs
    }

    [Fact]
    public void AssSubBuilder_KaraokeTranslation_OutputsKaraokeTags()
    {
        var config = new WorkflowConfig
        {
            InputFilePath = @"C:\x\movie.mp4",
            TargetLanguage = "zh",
            KaraokeMode = true
        };
        var context = new SubtitleWorkflowContext(config);
        context.State.CorrectedSentences =
        [
            new Sentence
            {
                Text = "Hello.",
                Start = 0,
                End = 2000,
                TranslatedText = "你好。",
                Words =
                [
                    new Word { Text = "Hello.", Start = 0, End = 2000, Speaker = string.Empty }
                ]
            }
        ];

        var ass = AssSubBuilder.FromWorkflow(context).Build().ToString();
        var dialogue = ass.Split('\n').First(line => line.StartsWith("Dialogue:"));

        Assert.Contains("\\K", dialogue);
        Assert.Contains("你", dialogue);
    }
}
