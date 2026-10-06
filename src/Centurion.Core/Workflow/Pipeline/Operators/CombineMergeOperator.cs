using Centurion.Abstractions.Pipeline;
using Centurion.Models;
using Centurion.Models.Ass;
using Centurion.Models.Console;
using Centurion.Models.Workflow;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Workflow.Pipeline.Operators;

/// <summary>
/// Sentence merging operator for the combine pipeline: interleaves all sources parsed by
/// <see cref="CombineParseOperator"/> by timeline into a single sentence set (stable sort: equal start times keep source order),
/// marks each sentence with <see cref="Sentence.Source"/>, and merges ASS style tables in source order (the first source wins for duplicate style names).
/// The result is written to CurrentSentences (consumed by downstream dedupe / quality report); raw source lists are removed from extension slots after consumption.
/// </summary>
public sealed class CombineMergeOperator(ILogger<CombineMergeOperator> logger)
    : PipelineOperatorBase<CombineMergeOperator>(logger)
{
    /// <summary>Operator name.</summary>
    public override string Name => "Combine Merge";

    /// <summary>
    /// Merges all source sentences and marks their origin.
    /// </summary>
    /// <param name="context">Workflow context (Extensions must contain the source lists).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">No mergeable sources in the extension slots.</exception>
    public override Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken)
    {
        if (context.State.Extensions.TryGetValue(CombineParseOperator.SourcesKey, out var raw)
            && raw is List<SubtitleSourceItem> sources)
        {
            context.State.Extensions.Remove(CombineParseOperator.SourcesKey);
            var merged = Merge(sources, context.State.Styles);

            context.State.CurrentSentences = merged;
            context.State.SubtitleSentences = [.. merged.Select(CloneSentence)];
            context.State.IsTranscribed = true;

            var counts = string.Join("; ", sources.Select(s => $"{s.Name}: {s.Sentences.Count}"));
            LogInfo($"Merged {sources.Count} source(s) ({counts}) into {merged.Count} sentences.");
            ConsoleServices.Output.WriteInfo(ConsoleServices.T("Merged {0} sources into {1} sentences.", sources.Count, merged.Count));
            return Task.CompletedTask;
        }

        throw new InvalidOperationException("Combine merge requires parsed sources in workflow state.");
    }

    /// <summary>
    /// Pure merge algorithm (shared by the operator and unit tests): sources stay ordered, then sorted
    /// by Start ascending (order preserved); each sentence gets its source tag; source style tables merge into the aggregated table (first source wins for duplicates).
    /// </summary>
    /// <param name="sources">Source lists, in input order.</param>
    /// <param name="styles">Aggregated style table (appended in place; usually State.Styles).</param>
    /// <returns>The merged sentence list.</returns>
    public static List<Sentence> Merge(IReadOnlyList<SubtitleSourceItem> sources, List<AssStyle> styles)
    {
        var merged = new List<Sentence>();
        var styleNames = new HashSet<string>(styles.Select(s => s.Name), StringComparer.OrdinalIgnoreCase);

        foreach (var source in sources)
        {
            // Merge ASS style tables: keep the first source's definition for duplicate style names
            foreach (var style in source.Styles)
                if (styleNames.Add(style.Name))
                    styles.Add(style);

            merged.AddRange(source.Sentences.Select(sentence =>
            {
                sentence.Source = source.Name;
                return sentence;
            }));
        }

        // Stable sort (.NET OrderBy preserves order): equal start times keep source order
        return [.. merged.OrderBy(s => s.Start)];
    }

    /// <summary>Deep-copies a sentence (keeping the Source tag, so the baseline copy and current set stay independent).</summary>
    private static Sentence CloneSentence(Sentence source) => new()
    {
        Text = source.Text,
        TranslatedText = source.TranslatedText,
        CleanedText = source.CleanedText,
        Start = source.Start,
        End = source.End,
        SkipRender = source.SkipRender,
        Style = source.Style,
        Source = source.Source,
        Words = source.Words.Select(word => new Word
        {
            Text = word.Text,
            Start = word.Start,
            End = word.End,
            Speaker = word.Speaker,
            PosTag = word.PosTag,
            Status = word.Status
        }).ToList()
    };
}
