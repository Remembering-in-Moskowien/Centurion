using Centurion.Core.Abstractions.Pipeline;
using Centurion.Core.Models;
using Centurion.Core.Models.Workflow;
using Centurion.Core.Text;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Pipeline.Operators;

public sealed class SubtitleTextCorrectorOperator(ILogger<SubtitleTextCorrectorOperator> logger)
    : PipelineOperatorBase<SubtitleTextCorrectorOperator>(logger)
{
    public override string Name => "Subtitle Text Correction";

    public override Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken)
    {
        var scripts = context.State.ScriptSentences;
        var subtitles = context.State.CurrentSentences;
        if (string.IsNullOrWhiteSpace(context.Config.ScriptFilePath) || scripts.Count == 0)
        {
            LogWarning("Text correction skipped because the script path or script sentences are missing.");
            return Task.CompletedTask;
        }

        var metadata = CorrectionMetadata.Get(context.State);
        var cursor = 0;
        var lowerBound = 0;
        for (var index = 0; index < subtitles.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var subtitle = subtitles[index];
            var bestIndex = -1;
            var bestRatio = 0d;
            var from = Math.Max(lowerBound, cursor - 3);
            var to = Math.Min(scripts.Count - 1, cursor + 3);
            for (var candidate = from; candidate <= to; candidate++)
            {
                var ratio = TextSimilarity.Similarity(
                    Tokenizer.Normalize(subtitle.CleanedText ?? subtitle.Text),
                    Tokenizer.Normalize(scripts[candidate].CleanedText ?? scripts[candidate].Text));
                if (ratio > bestRatio)
                {
                    bestRatio = ratio;
                    bestIndex = candidate;
                }
            }

            if (bestIndex < 0 || bestRatio < context.Config.FuzzyThreshold)
            {
                context.State.Report.Unmatched++;
                Logger.LogDebug("No script match found for subtitle {Index}; best similarity was {Similarity:F3}.", index + 1, bestRatio);
                SetAction(metadata, subtitle, "kept");
                continue;
            }

            subtitle.CleanedText = scripts[bestIndex].CleanedText ?? scripts[bestIndex].Text;
            subtitle.Text = subtitle.CleanedText;
            SetMetadata(metadata, subtitle, CorrectKeys.MatchRatio, bestRatio);
            SetAction(metadata, subtitle, "retexted");
            context.State.Report.TextCorrected++;
            cursor = bestIndex + 1;
            lowerBound = cursor;
        }

        context.State.CurrentSentences = subtitles;
        OnProgress(100, "Text correction completed");
        return Task.CompletedTask;
    }

    private static void SetAction(Dictionary<Sentence, Dictionary<string, object>> metadata, Sentence sentence, string action) =>
        SetMetadata(metadata, sentence, CorrectKeys.Action, action);

    private static void SetMetadata(Dictionary<Sentence, Dictionary<string, object>> metadata, Sentence sentence, string key, object value)
    {
        if (!metadata.TryGetValue(sentence, out var values))
            metadata[sentence] = values = new Dictionary<string, object>();
        values[key] = value;
    }
}
