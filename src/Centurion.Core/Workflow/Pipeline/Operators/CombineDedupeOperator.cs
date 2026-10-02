using Centurion.Abstractions.Pipeline;
using Centurion.Core.Processing.Text;
using Centurion.Models;
using Centurion.Models.Console;
using Centurion.Models.Workflow;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace Centurion.Core.Workflow.Pipeline.Operators;

/// <summary>
/// 合并管道的去重算子：不同字幕轨常含内容重复的行（SDH 字幕、双语拆轨、
/// 同一台词的不同版本）。两条句子时间窗接近（相交或间隙 ≤ 容差）
/// 且文本相似度达到阈值时视为重复，保留时间更早/来源更靠前的一条，其余标记跳过渲染。
/// 默认启用；容差与阈值由命令参数配置。
/// </summary>
public sealed partial class CombineDedupeOperator(ILogger<CombineDedupeOperator> logger)
    : PipelineOperatorBase<CombineDedupeOperator>(logger)
{
    /// <summary>State.Extensions 中去重时间容差（毫秒）的键。</summary>
    public const string ToleranceKey = "CombineDedupeToleranceMs";

    /// <summary>State.Extensions 中相似度阈值的键。</summary>
    public const string SimilarityKey = "CombineDedupeSimilarity";

    /// <summary>算子名称。</summary>
    public override string Name => "Combine Dedupe";

    /// <summary>
    /// 对当前句子集合执行贪心去重。
    /// </summary>
    /// <param name="context">工作流上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
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
    /// 纯去重算法（供算子与单元测试共用）：按输入顺序贪心扫描，
    /// 与已保留句子时间接近且文本相似的句子置 <see cref="Sentence.SkipRender"/>。
    /// </summary>
    /// <param name="sentences">合并后的句子集合（就地标记）。</param>
    /// <param name="toleranceMs">时间窗容差（毫秒）。</param>
    /// <param name="similarityThreshold">文本相似度阈值（0-1）。</param>
    /// <returns>被标记跳过的句子数。</returns>
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

    /// <summary>时间窗是否接近：区间相交，或间隙不超过容差。</summary>
    private static bool TimesClose(Sentence a, Sentence b, double toleranceMs) =>
        a.Start <= b.End + toleranceMs && b.Start <= a.End + toleranceMs;

    /// <summary>归一化比较文本：剔除 ASS 覆盖标签、换行、空白并统一大小写。</summary>
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
