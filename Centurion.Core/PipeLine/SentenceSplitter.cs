using Centurion.Core.Abstractions;
using Centurion.Core.Abstractions.Factories;
using Centurion.Core.Abstractions.Strategy;
using Centurion.Core.Models;
using Centurion.Core.Strategy.SentenceSplit;

namespace Centurion.Core.PipeLine;

/// <summary>
/// 分句算子，通过工厂动态选择分句策略（启发式/规则/LLM 等）。
/// </summary>
public class SentenceSplitOperator(ISentenceSplitStrategyFactory factory) : PipelineOperatorBase
{
    private readonly ISentenceSplitStrategyFactory _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    public override string Name => "Sentence Splitting";

    public override async Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken)
    {
        if (context.State.IsSplit)
        {
            LogInfo("Split results already exist, skipping.");
            return;
        }

        var config = context.Config;

        var inputSentences = context.State.DiarizedSentences?.Count > 0
            ? context.State.DiarizedSentences
            : context.State.CoarseSentences?.Count > 0
                ? context.State.CoarseSentences
                : context.State.TranscribeSentences;

        if (inputSentences == null || inputSentences.Count == 0)
        {
            LogWarning("No sentences to split.");
            context.State.SplitSentences = [];
            context.State.IsSplit = true;
            return;
        }

        var options = new SplitOptions
        {
            MaxLength = config.MaxSentenceLength,
            TargetLength = config.TargetSentenceLength,
            SpreadRange = config.SpreadRange,
            MergeGap = config.MergeGapSeconds,
            EnablePunctuationRewrite = config.EnablePunctuationRewrite,
            Language = config.Language,
            ModelCachePath = config.CacheDirectory,
            ChunkGranularity = Math.Clamp(config.ChunkGranularity, 0f, 1f)
        };

        // 通过工厂创建分句策略，传递模型和 API Key（仅 LLM 策略需要）
        var strategy = _factory.Create(
            config.SplitStrategy,
            options,
            config.SplitterModel,
            config.SplitterApiKey
        );

        LogInfo($"Using split strategy: {strategy.GetType().Name}");

        if (config.SplitStrategy.Equals("nlp", StringComparison.OrdinalIgnoreCase) ||
            config.SplitStrategy.Equals("catalyst", StringComparison.OrdinalIgnoreCase))
        {
            context.State.SplitSentences = await ((CatalystSplitStrategy)strategy).Split(inputSentences, options);
            context.State.IsSplit = true;
            LogInfo($"Split into {context.State.SplitSentences.Count} sentences.");
            return;
        }

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