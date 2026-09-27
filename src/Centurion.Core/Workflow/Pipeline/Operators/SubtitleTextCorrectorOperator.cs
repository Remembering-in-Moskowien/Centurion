using Centurion.Abstractions.Pipeline;
using Centurion.Models;
using Centurion.Models.Workflow;
using Centurion.Core.Processing.Text;using Microsoft.Extensions.Logging;

namespace Centurion.Core.Workflow.Pipeline.Operators;

/// <summary>
/// 字幕文本校正算子：将当前字幕句与脚本句做模糊相似度匹配，
/// 命中后用脚本文本覆盖字幕文本，未命中则保留原文并计入报告。
/// </summary>
public sealed class SubtitleTextCorrectorOperator(ILogger<SubtitleTextCorrectorOperator> logger)
    : PipelineOperatorBase<SubtitleTextCorrectorOperator>(logger)
{
    /// <summary>算子在管道中的显示名称。</summary>
    public override string Name => "Subtitle Text Correction";

    /// <summary>
    /// 执行文本校正：逐句在脚本中按滑动窗口找最佳匹配，覆盖命中文本并记录匹配比例与统计。
    /// </summary>
    /// <param name="context">字幕工作流上下文，提供字幕句、脚本句与配置阈值。</param>
    /// <param name="cancellationToken">用于取消校正过程的取消标记。</param>
    public override Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken)
    {
        var scripts = context.State.ScriptSentences;
        var subtitles = context.State.CurrentSentences;
        if (string.IsNullOrWhiteSpace(context.Config.ScriptFilePath) || scripts.Count == 0)
        {
            LogWarning("Text correction skipped because the script path or script sentences are missing.");
            return Task.CompletedTask;
        }

        var metadata = context.State.CorrectionMetadata;
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
