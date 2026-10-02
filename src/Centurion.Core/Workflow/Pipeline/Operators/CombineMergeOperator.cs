using Centurion.Abstractions.Pipeline;
using Centurion.Models;
using Centurion.Models.Ass;
using Centurion.Models.Console;
using Centurion.Models.Workflow;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Workflow.Pipeline.Operators;

/// <summary>
/// 合并管道的句子合并算子：把 <see cref="CombineParseOperator"/> 解析出的全部来源
/// 按时间轴交错合并为单一句子集合（稳定排序：同起始时间保持来源顺序），
/// 每条句子标记 <see cref="Sentence.Source"/>，ASS 样式表按来源顺序合并（同名样式首个来源优先）。
/// 结果写入 CurrentSentences（下游去重/质量报告消费），来源原始列表消费后从扩展槽移除。
/// </summary>
public sealed class CombineMergeOperator(ILogger<CombineMergeOperator> logger)
    : PipelineOperatorBase<CombineMergeOperator>(logger)
{
    /// <summary>算子名称。</summary>
    public override string Name => "Combine Merge";

    /// <summary>
    /// 合并全部来源句子并标记来源。
    /// </summary>
    /// <param name="context">工作流上下文（Extensions 须含来源列表）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <exception cref="InvalidOperationException">扩展槽中没有可合并的来源。</exception>
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
    /// 纯合并算法（供算子与单元测试共用）：来源顺序稳定 → 按 Start 升序（次序保持）；
    /// 每条句子打来源标记；来源样式表并入样式聚合表（同名首个优先）。
    /// </summary>
    /// <param name="sources">来源列表（按输入顺序）。</param>
    /// <param name="styles">样式聚合表（就地追加；通常传 State.Styles）。</param>
    /// <returns>合并后的句子列表。</returns>
    public static List<Sentence> Merge(IReadOnlyList<SubtitleSourceItem> sources, List<AssStyle> styles)
    {
        var merged = new List<Sentence>();
        var styleNames = new HashSet<string>(styles.Select(s => s.Name), StringComparer.OrdinalIgnoreCase);

        foreach (var source in sources)
        {
            // ASS 样式表合并：同名样式保留首个来源的定义
            foreach (var style in source.Styles)
                if (styleNames.Add(style.Name))
                    styles.Add(style);

            merged.AddRange(source.Sentences.Select(sentence =>
            {
                sentence.Source = source.Name;
                return sentence;
            }));
        }

        // 稳定排序（.NET OrderBy 保证次序保持）：同起始时间保持来源顺序
        return [.. merged.OrderBy(s => s.Start)];
    }

    /// <summary>深拷贝句子（保留 Source 标记，基线副本与当前集合互不干扰）。</summary>
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
