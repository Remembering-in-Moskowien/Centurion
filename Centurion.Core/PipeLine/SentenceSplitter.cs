using Centurion.Core.Abstractions;
using Centurion.Core.Abstractions.Factories;
using Centurion.Core.Abstractions.Strategy;
using Centurion.Core.Models;

namespace Centurion.Core.PipeLine;

/// <summary>
/// 分句算子，通过工厂动态选择分句策略（启发式/规则/LLM 等）。
/// </summary>
public class SentenceSplitOperator : PipelineOperatorBase
{
    private readonly ISentenceSplitStrategyFactory _factory;

    public override string Name => "Sentence Splitting";

    public SentenceSplitOperator(ISentenceSplitStrategyFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public override async Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken)
    {
        // 检查点
        if (context.State.IsSplit)
        {
            LogInfo("Split results already exist, skipping.");
            return;
        }

        var config = context.Config;

        // 输入：优先使用说话人分割后的结果，否则使用粗分句或原始转录结果
        var inputSentences = context.State.DiarizedSentences?.Count > 0
            ? context.State.DiarizedSentences
            : context.State.CoarseSentences?.Count > 0
                ? context.State.CoarseSentences
                : context.State.WhisperSentences;

        if (inputSentences == null || inputSentences.Count == 0)
        {
            LogWarning("No sentences to split.");
            context.State.SplitSentences = new List<Sentence>();
            context.State.IsSplit = true;
            return;
        }

        // 构建分句选项
        var options = new SplitOptions
        {
            MaxLength = config.MaxSentenceLength,
            TargetLength = config.TargetSentenceLength,
            SpreadRange = config.SpreadRange,
            MergeGap = config.MergeGapSeconds,
            EnablePunctuationRewrite = config.EnablePunctuationRewrite,
            Language = config.Language,
            ModelCachePath = config.CacheDirectory
        };

        // 通过工厂创建分句策略
        var strategy = _factory.Create(config.SplitStrategy, options);
        LogInfo($"Using split strategy: {strategy.GetType().Name}");

        var allSplit = new List<Sentence>();
        foreach (var sentence in inputSentences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (sentence.Words == null || sentence.Words.Count == 0)
                continue;

            var result = await strategy.Split(sentence.Words, options);
            allSplit.AddRange(result);
        }

        context.State.SplitSentences = allSplit;
        context.State.IsSplit = true;
        LogInfo($"Split into {allSplit.Count} sentences.");
    }
}