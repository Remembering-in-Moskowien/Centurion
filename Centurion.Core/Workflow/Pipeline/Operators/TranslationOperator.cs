using Centurion.Abstractions.Pipeline;
using Centurion.Abstractions.Strategy;
using Centurion.Models;
using Centurion.Models.Workflow;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Workflow.Pipeline.Operators;

/// <summary>
/// 翻译算子：把当前工作集句子翻译到目标语言（策略内部按批并行调用 LLM），
/// 译文写回各句 TranslatedText，时间轴与词级明细保持不变。
/// 由 TranslateCommand 与 pipeline graph 命令共享装配。
/// </summary>
public sealed class TranslationOperator(
    ITranslationStrategy strategy,
    TranslationOptions options,
    ILogger<TranslationOperator> logger) : PipelineOperatorBase<TranslationOperator>(logger)
{
    private readonly ITranslationStrategy _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
    private readonly TranslationOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    /// <summary>算子在管道中的显示名称。</summary>
    public override string Name => "Translation";

    /// <summary>执行翻译并更新工作流状态（TranslatedSentences / CurrentSentences / IsTranslated）。</summary>
    public override async Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken)
    {
        var sentences = context.State.CurrentSentences;
        if (sentences.Count == 0)
        {
            LogInfo("No sentences to translate; skipping translation.");
            context.State.IsTranslated = true;
            return;
        }

        OnProgress(10, $"Translating {sentences.Count} sentences to {_options.TargetLanguage}");
        LogInfo($"Using translation strategy: {_strategy.StrategyName} (batch size {_options.BatchSize}, concurrency {_options.MaxConcurrency})");

        await _strategy.TranslateAsync(sentences, _options, cancellationToken);

        context.State.TranslatedSentences = sentences;
        context.State.CurrentSentences = sentences;
        context.State.IsTranslated = true;
        OnProgress(100, "Translation completed");
        LogInfo($"Translation completed ({_options.TargetLanguage}).");
    }
}
