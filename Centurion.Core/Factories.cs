using Centurion.Core.Abstractions.Factories;
using Centurion.Core.Abstractions.Strategy;
using Centurion.Core.Strategy.Alignment;
using Centurion.Core.Strategy.SentenceSplit;
using Centurion.Core.Strategy.Transcribe;
using Microsoft.Extensions.DependencyInjection;

namespace Centurion.Core;

/// <summary>
/// 转录策略工厂实现
/// </summary>
public class TranscriptionStrategyFactory : ITranscriptionStrategyFactory
{
    private readonly IServiceProvider _serviceProvider;

    public TranscriptionStrategyFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public ITranscriptionStrategy Create(string engine, string? model, string language, string? initialPrompt)
    {
        ITranscriptionStrategy strategy = engine.ToLowerInvariant() switch
        {
            "whisper" => _serviceProvider.GetRequiredService<FasterWhisperStrategy>(),
            _ => throw new NotSupportedException($"Transcription engine '{engine}' is not supported.")
        };

        return strategy;
    }
}

/// <summary>
/// 分句策略工厂实现
/// </summary>
public class SentenceSplitStrategyFactory : ISentenceSplitStrategyFactory
{
    private readonly IServiceProvider _serviceProvider;

    public SentenceSplitStrategyFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public ISentenceSplitStrategy Create(string strategy, SplitOptions options)
    {
        ISentenceSplitStrategy instance = strategy.ToLowerInvariant() switch
        {
            "rule"      => _serviceProvider.GetRequiredService<RuleBasedSplitStrategy>(),
            _ => throw new NotSupportedException($"Split strategy '{strategy}' is not supported.")
        };

        return instance;
    }
}

/// <summary>
/// 对齐策略工厂实现
/// </summary>
public class AlignmentStrategyFactory : IAlignmentStrategyFactory
{
    private readonly IServiceProvider _serviceProvider;

    public AlignmentStrategyFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public IAlignmentStrategy Create(string engine, string? model)
    {
        IAlignmentStrategy instance = engine.ToLowerInvariant() switch
        {
            "qwen"   => _serviceProvider.GetRequiredService<NoOpAlignmentStrategy>(),
            _ => throw new NotSupportedException($"Alignment engine '{engine}' is not supported.")
        };

        return instance;
    }
}