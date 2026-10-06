using Centurion.Abstractions.Pipeline;
using Centurion.Models.Workflow;
using Microsoft.Extensions.Logging;
using Centurion.Core.Utils.Parsing;
namespace Centurion.Core.Workflow.Pipeline.Operators;

/// <summary>
/// Timeline overlap resolution operator: sorts the current sentence list by time and removes overlaps between adjacent sentences,
/// ensuring the final subtitle timeline is strictly monotonic (a later block's start time is never before the previous block's end time).
/// </summary>
public sealed class OverlapResolutionOperator : PipelineOperatorBase<OverlapResolutionOperator>
{
    /// <summary>Creates a timeline overlap resolution operator instance.</summary>
    /// <param name="logger">Logger that records the resolution process.</param>
    public OverlapResolutionOperator(ILogger<OverlapResolutionOperator> logger) : base(logger)
    {
    }

    /// <summary>Display name of the operator in the pipeline.</summary>
    public override string Name => "Resolve Overlaps";

    /// <summary>
    /// Resolves timeline overlaps among the current sentences of <see cref="SubtitleWorkflowContext"/>,
    /// fixing and reordering the list in place; keeps it unchanged when there are no overlaps.
    /// </summary>
    /// <param name="context">Subtitle workflow context, providing the current sentence list.</param>
    /// <param name="cancellationToken">Cancellation token used to cancel the resolution process.</param>
    public override Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var sentences = context.State.CurrentSentences;
        if (sentences.Count < 2)
        {
            LogInfo("Skipping overlap resolution: fewer than 2 sentences.");
            return Task.CompletedTask;
        }

        var changed = TimelineOverlapResolver.Resolve(sentences);
        if (changed > 0)
        {
            LogInfo($"Resolved overlaps for {changed} sentences.");
            context.State.Warnings.Add($"Overlap resolution adjusted {changed} sentence timing(s).");
        }
        else
        {
            LogInfo("No overlapping subtitles detected.");
        }

        return Task.CompletedTask;
    }
}
