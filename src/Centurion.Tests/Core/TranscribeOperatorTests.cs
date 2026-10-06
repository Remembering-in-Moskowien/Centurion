using Centurion.Models;
using Centurion.Core.Workflow.Pipeline.Operators;
using Xunit;

namespace Centurion.Tests.Core;

/// <summary>
/// Tests for transcription word-stream post-processing: deduplicating leading word repeats (qwen3's segmented decoding re-emits leading tokens at segment boundaries).
/// </summary>
public class TranscribeOperatorTests
{
    [Fact]
    public void DeduplicateWordRepeats_RemovesIdenticalAdjacentWord()
    {
        var words = new List<Word>
        {
            new() { Text = "Jetzt", Start = 57000, End = 57240, Speaker = "SPEAKER_00"},
            new() { Text = "Jetzt", Start = 57000, End = 57240, Speaker = "SPEAKER_00"},  // repeated leading word (same text and timestamps)
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
            new() { Text = "Ganze", Start = 139530, End = 141000, Speaker = "SPEAKER_00"}  // a genuine word repeat (different timestamps) → kept
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
            new() { Text = "jetzt", Start = 57000, End = 57240, Speaker = "SPEAKER_00"}  // different casing → not treated as a repeat
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
