using Centurion.Models;
using Centurion.Core.Workflow.Pipeline.Operators;
using Xunit;

namespace Centurion.Tests.Core;

/// <summary>
/// 转录词流后处理测试：句首重复词去重（qwen3 分段解码段边界重复发射句首 token）。
/// </summary>
public class TranscribeOperatorTests
{
    [Fact]
    public void DeduplicateWordRepeats_RemovesIdenticalAdjacentWord()
    {
        var words = new List<Word>
        {
            new() { Text = "Jetzt", Start = 57000, End = 57240, Speaker = "SPEAKER_00"},
            new() { Text = "Jetzt", Start = 57000, End = 57240, Speaker = "SPEAKER_00"},  // 句首重复（同文本同时间戳）
            new() { Text = "sind", Start = 62120, End = 62120, Speaker = "SPEAKER_00"},
            new() { Text = "sie", Start = 62120, End = 62360, Speaker = "SPEAKER_00"}
        };

        var result = TranscribeOperator.DeduplicateWordRepeats(words);

        Assert.Equal(3, result.Count);
        Assert.Equal("sind", result[1].Text);
    }

    [Fact]
    public void DeduplicateWordRepeats_KeepsRealRepeatedWordsWithDifferentTimestamps()
    {
        var words = new List<Word>
        {
            new() { Text = "Ganze", Start = 138000, End = 139530, Speaker = "SPEAKER_00"},
            new() { Text = "Ganze", Start = 139530, End = 141000, Speaker = "SPEAKER_00"}  // 真实叠词（时间不同）→ 保留
        };

        var result = TranscribeOperator.DeduplicateWordRepeats(words);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void DeduplicateWordRepeats_KeepsWordsWithSameTextDifferentCase()
    {
        var words = new List<Word>
        {
            new() { Text = "Jetzt", Start = 57000, End = 57240, Speaker = "SPEAKER_00"},
            new() { Text = "jetzt", Start = 57000, End = 57240, Speaker = "SPEAKER_00"}  // 大小写不同 → 不视为重复
        };

        var result = TranscribeOperator.DeduplicateWordRepeats(words);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void GroupIntoSentences_AfterDedup_NoDuplicateAtSentenceStart()
    {
        var words = new List<Word>
        {
            new() { Text = "Jetzt", Start = 57000, End = 57240, Speaker = "SPEAKER_00"},
            new() { Text = "Jetzt", Start = 57000, End = 57240, Speaker = "SPEAKER_00"},
            new() { Text = "sind", Start = 62120, End = 62120, Speaker = "SPEAKER_00"},
            new() { Text = "sie", Start = 62120, End = 62360, Speaker = "SPEAKER_00"},
            new() { Text = "Schlafe.", Start = 67800, End = 69320, Speaker = "SPEAKER_00"}
        };

        var sentences = TranscribeOperator.GroupIntoSentences(TranscribeOperator.DeduplicateWordRepeats(words));

        Assert.Single(sentences);
        Assert.Equal("JetztsindsieSchlafe.", sentences[0].Text);
    }

    [Fact]
    public void DeduplicateWordRepeats_EmptyInput_ReturnsEmpty()
    {
        var result = TranscribeOperator.DeduplicateWordRepeats(new List<Word>());
        Assert.Empty(result);
    }
}
