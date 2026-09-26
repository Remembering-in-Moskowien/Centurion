using Centurion.Abstractions;
using Centurion.Abstractions.Pipeline;
using Centurion.Abstractions.Strategy;
using Centurion.Models;
using Centurion.Models.Workflow;
using Centurion.Core.Workflow.Strategy.SentenceSplit;using Microsoft.Extensions.Logging;

namespace Centurion.Core.Workflow.Pipeline.Operators;

/// <summary>
/// 分句算子，使用管线组装阶段注入的策略及选项（规则/LLM 等）。
/// </summary>
public class SentenceSplitOperator(
    ISentenceSplitStrategy strategy,
    SplitOptions options,
    ILogger<SentenceSplitOperator> logger) : PipelineOperatorBase<SentenceSplitOperator>(logger)
{
    private readonly ISentenceSplitStrategy _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
    private readonly SplitOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    /// <summary>算子在管道中的显示名称。</summary>
    public override string Name => "Sentence Splitting";

    /// <summary>
    /// 执行分句：按配置经工厂创建分句策略，把当前词流切分为句子并写回工作流状态。
    /// </summary>
    /// <param name="context">字幕工作流上下文，提供输入词流与分句配置。</param>
    /// <param name="cancellationToken">用于取消分句过程的取消标记。</param>
    public override async Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken)
    {
        if (context.State.IsSplit)
        {
            LogInfo("Split results already exist, skipping.");
            context.State.CurrentSentences = context.State.SplitSentences;
            return;
        }

        var inputSentences = context.State.DiarizedSentences?.Count > 0
            ? context.State.DiarizedSentences
            : context.State.CoarseSentences?.Count > 0
                ? context.State.CoarseSentences
                : context.State.TranscribeSentences;

        if (inputSentences == null || inputSentences.Count == 0)
        {
            const string message = "No current sentences are available for splitting.";
            context.State.Errors.Add(message);
            LogWarning(message);
            context.State.SplitSentences = [];
            context.State.CurrentSentences = context.State.SplitSentences;
            throw new InvalidOperationException(message);
        }

        LogInfo($"Using split strategy: {_strategy.GetType().Name}");

        var allSplit = new List<Sentence>();
        foreach (var sentence in inputSentences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (sentence.Words == null || sentence.Words.Count == 0)
                continue;

            var result = await _strategy.Split(sentence.Words, _options);
            allSplit.AddRange(result);
        }

        context.State.SplitSentences = allSplit;
        context.State.CurrentSentences = context.State.SplitSentences;
        context.State.IsSplit = true;
        LogInfo($"Split into {allSplit.Count} sentences.");
    }
}
