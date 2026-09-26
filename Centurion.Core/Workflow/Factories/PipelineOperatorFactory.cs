using Centurion.Abstractions.Factories;
using Centurion.Abstractions.Providers;
using Centurion.Abstractions.Strategy;
using Centurion.Core.Capabilities.Infrastructure.Asr;
using Centurion.Core.Providers;using Centurion.Core.Workflow.Pipeline.Operators;using Centurion.Core.Workflow.Strategy.Alignment;using Centurion.Core.Workflow.Strategy.Diarization;using Centurion.Models.Asr;
using Centurion.Models.Llm;
using Centurion.Models.Metadata;
using Centurion.Models.Workflow;
using Microsoft.Extensions.DependencyInjection;

namespace Centurion.Core.Workflow.Factories;

/// <summary>按工作流配置预解析策略并组装对应算子，供管线启动前调用。</summary>
public sealed class PipelineOperatorFactory(
    IServiceProvider serviceProvider,
    ISentenceSplitStrategyFactory sentenceSplitFactory,
    IDiarizationStrategyFactory diarizationFactory,
    IAlignmentStrategyFactory alignmentFactory,
    ModelRegistry modelRegistry,
    IProviderFactory providerFactory)

{
    /// <summary>创建已注入转录策略的算子。</summary>
    public TranscribeOperator CreateTranscribeOperator(WorkflowConfig config)
    {
        var asrOptions = new AsrOptions(
            AsrEndpointParser.Resolve(config.AsrProvider)?.Provider ?? AsrProvider.OpenAI,
            config.AsrApiKey,
            config.AsrBaseUrl);
        var chain = providerFactory.CreateAsrChain(
            config.TranscriberEngine,
            config.TranscriberModel,
            asrOptions,
            ProviderProfileResolver.Current);
        return ActivatorUtilities.CreateInstance<TranscribeOperator>(serviceProvider, chain);
    }

    /// <summary>创建已注入分句策略及分句选项的算子。</summary>
    public SentenceSplitOperator CreateSentenceSplitOperator(WorkflowConfig config)
    {
        var options = CreateSplitOptions(config);
        var strategy = sentenceSplitFactory.Create(
            config.SplitStrategy,
            options,
            new LlmOptions
            {
                Model = config.SplitterModel,
                ApiKey = config.SplitterApiKey,
                ProviderName = config.SplitterProvider,
                BaseUrl = config.SplitterBaseUrl
            });
        return ActivatorUtilities.CreateInstance<SentenceSplitOperator>(serviceProvider, strategy, options);
    }

    /// <summary>创建启用时已注入说话人分割策略的算子；关闭时不组装该阶段。</summary>
    public DiarizationOperator? CreateDiarizationOperator(WorkflowConfig config)
    {
        if (string.Equals(config.DiarizationBackend, "none", StringComparison.OrdinalIgnoreCase))
            return null;

        var strategy = diarizationFactory.Create(config.DiarizationBackend);
        if (strategy is CrispAsrDiarizationStrategy crispStrategy
            && !string.IsNullOrWhiteSpace(config.DiarizationMethod)
            && !string.Equals(config.DiarizationMethod, "pyannote", StringComparison.OrdinalIgnoreCase))
            crispStrategy.Method = config.DiarizationMethod;

        return ActivatorUtilities.CreateInstance<DiarizationOperator>(serviceProvider, strategy);
    }

    /// <summary>创建启用时已注入对齐策略的算子；关闭时不组装该阶段。</summary>
    public AlignmentOperator? CreateAlignmentOperator(WorkflowConfig config)
    {
        if (!config.EnableAlignment)
            return null;

        var modelName = config.AlignmentModel ?? "qwen3-forced-aligner-0.6b";
        if (!modelRegistry.Qwen3ForcedAlignerModels.ContainsKey(modelName))
            throw new NotSupportedException($"Alignment model '{modelName}' is not registered.");

        var strategy = alignmentFactory.Create(modelName);
        if (strategy is CrispAsrAlignmentStrategy crispStrategy)
        {
            crispStrategy.ChunkGapSeconds = config.AlignmentChunkGapSeconds;
            crispStrategy.MaxChunkSeconds = config.AlignmentMaxChunkSeconds;
        }

        return ActivatorUtilities.CreateInstance<AlignmentOperator>(serviceProvider, strategy);
    }

    private static SplitOptions CreateSplitOptions(WorkflowConfig config) => new()
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
}