using Centurion.Abstractions.Pipeline;
using Centurion.Core.Processing.Text;
using Centurion.Models;
using Centurion.Models.Console;
using Centurion.Models.Workflow;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace Centurion.Core.Workflow.Pipeline.Operators;

/// <summary>
/// Deduplication operator for the combine pipeline: different subtitle tracks often carry repeated
/// lines (SDH subtitles, split bilingual tracks, alternate versions of the same dialogue). Two sentences
/// are duplicates when their time windows are close (overlapping or gap within tolerance) and text similarity reaches the threshold; the earlier / earlier-source one is kept, the rest are marked SkipRender.
/// Enabled by default; tolerance and threshold are configured via command arguments.
/// </summary>
public sealed partial class CombineDedupeOperator(ILogger<CombineDedupeOperator> logger)
    : PipelineOperatorBase<CombineDedupeOperator>(logger)
{
    /// <summary>Key in State.Extensions for the dedupe time tolerance (ms).</summary>
    public const string ToleranceKey = "CombineDedupeToleranceMs";

    /// <summary>Key in State.Extensions for the similarity threshold.</summary>
    public const string SimilarityKey = "CombineDedupeSimilarity";

    /// <summary>Operator name.</summary>
    public override string Name => "Combine Dedupe";

    /// <summary>
    /// Runs greedy deduplication on the current sentence set.
    /// </summary>
    /// <param name="context">Workflow context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public override Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken)
    {
        var tolerance = GetExtensionValue(context, ToleranceKey, 500.0);
        var threshold = GetExtensionValue(context, SimilarityKey, 0.7);

        var removed = Deduplicate(context.State.CurrentSentences, tolerance, threshold);

        if (removed > 0)
        {
            LogInfo($"Dedupe removed {removed} duplicate sentence(s) (tolerance {tolerance:F0} ms, similarity >= {threshold:F2}).");
            ConsoleServices.Output.WriteInfo(ConsoleServices.T("Deduplicated {0} repeating sentences.", removed));
        }
        else
        {
            LogInfo("Dedupe found no duplicate sentences.");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Pure deduplication algorithm (shared by the operator and unit tests): scans greedily in input
    /// order and marks with <see cref="Sentence.SkipRender"/> sentences close in time to a kept sentence and similar in text.
    /// </summary>
    /// <param name="sentences">Merged sentence set (marked in place).</param>
    /// <param name="toleranceMs">Time-window tolerance (milliseconds).</param>
    /// <param name="similarityThreshold">Text similarity threshold (0-1).</param>
    /// <returns>The number of sentences marked to skip rendering.</returns>
    public static int Deduplicate(List<Sentence> sentences, double toleranceMs, double similarityThreshold)
    {
        var kept = new List<Sentence>(sentences.Count);
        var removed = 0;

        foreach (var candidate in sentences)
        {
            var isDuplicate = kept.Any(existing =>
                !existing.SkipRender
                && TimesClose(existing, candidate, toleranceMs)
                && TextSimilarity.Similarity(Normalize(existing), Normalize(candidate)) >= similarityThreshold);

            if (isDuplicate)
            {
                candidate.SkipRender = true;
                removed++;
            }
            else
            {
                kept.Add(candidate);
            }
        }

        return removed;
    }

    /// <summary>Whether time windows are close: intervals overlap, or the gap is within tolerance.</summary>
    private static bool TimesClose(Sentence a, Sentence b, double toleranceMs) =>
        a.Start <= b.End + toleranceMs && b.Start <= a.End + toleranceMs;

    /// <summary>Normalizes comparison text: strips ASS override tags, line breaks and whitespace, and lowercases.</summary>
    private static string Normalize(Sentence sentence)
    {
        var text = sentence.Text ?? string.Empty;
        text = AssOverrideRegex().Replace(text, string.Empty);
        text = text.Replace("\\N", " ", StringComparison.Ordinal)
                   .Replace("\\n", " ", StringComparison.Ordinal)
                   .Replace("\\h", " ", StringComparison.Ordinal);
        return WhitespaceRegex().Replace(text, " ").Trim().ToLowerInvariant();
    }

    private static double GetExtensionValue(SubtitleWorkflowContext context, string key, double fallback) =>
        context.State.Extensions.TryGetValue(key, out var raw) && raw is double v ? v : fallback;

    [GeneratedRegex(@"\{[^}]*\}")]
    private static partial Regex AssOverrideRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
