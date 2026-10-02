using Centurion.Cli.Commands;
using Centurion.Core.Workflow.Pipeline.Operators;
using Centurion.Models;
using Centurion.Models.Ass;
using Centurion.Models.Workflow;
using Xunit;

namespace Centurion.Tests.Core;

public sealed class CombineOperatorTests
{
    [Fact]
    public void Merge_SortsByStartAndMarksSources()
    {
        var sources = new List<SubtitleSourceItem>
        {
            new() { Name = "eng.ass", Sentences = [new Sentence { Text = "Hello", Start = 1000, End = 2000 }] },
            new() { Name = "zh.ass", Sentences = [new Sentence { Text = "你好", Start = 0, End = 900 }] }
        };

        var styles = new List<AssStyle>();
        var merged = CombineMergeOperator.Merge(sources, styles);

        Assert.Equal(2, merged.Count);
        Assert.Equal("zh.ass", merged[0].Source);
        Assert.Equal("eng.ass", merged[1].Source);
    }

    [Fact]
    public void Deduplicate_RemovesNearDuplicateSentence()
    {
        var sentences = new List<Sentence>
        {
            new() { Text = "Hello world", Start = 0, End = 1000 },
            new() { Text = "Hello world", Start = 800, End = 1800 }
        };

        var removed = CombineDedupeOperator.Deduplicate(sentences, 500, 0.9);

        Assert.Equal(1, removed);
        Assert.True(sentences[1].SkipRender);
    }

    [Fact]
    public void CombineCommand_ExistsInCliNamespace()
    {
        var type = typeof(CombineCommand);
        Assert.Equal("CombineCommand", type.Name);
    }
}
