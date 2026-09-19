using Centurion.Abstractions.Factories;
using Centurion.Abstractions.Strategy;
using Centurion.Core.Strategy.SentenceSplit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Factories;

/// <summary>
/// Sentence split strategy factory: rule-based or LLM-based (OpenAI/Ollama)
/// </summary>
public class SentenceSplitStrategyFactory(
    IServiceProvider serviceProvider,
    ILogger<SentenceSplitStrategyFactory> logger)
    : ISentenceSplitStrategyFactory
{
    /// <summary>
    /// 按策略类型创建分句策略，支持规则式与基于大语言模型（OpenAI/Ollama）两种模式。
    /// </summary>
    /// <param name="strategy">分句策略名称，支持 "rule"、"catalyst"/"nlp"（规则式）与 "llm"。</param>
    /// <param name="options">分句所需的规则选项，供规则式或 LLM 策略使用。</param>
    /// <param name="model">可选的模型名称；未提供时按各后端默认模型处理。</param>
    /// <param name="apiKey">可选的 API 密钥；提供时使用 OpenAI 后端，否则回退到本地 Ollama。</param>
    /// <returns>对应的分句策略实例。</returns>
    /// <exception cref="NotSupportedException">当策略名称不受支持时抛出。</exception>
    public ISentenceSplitStrategy Create(string strategy, SplitOptions options, string? model = null, string? apiKey = null)
    {
        return strategy.ToLowerInvariant() switch
        {
            "rule" => serviceProvider.GetRequiredService<RuleBasedSplitStrategy>(),
            "catalyst" or "nlp" => serviceProvider.GetRequiredService<RuleBasedSplitStrategy>(),
            "llm" => CreateLLMStrategy(options, model, apiKey),
            _ => throw new NotSupportedException($"Split strategy '{strategy}' is not supported.")
        };
    }

    private ISentenceSplitStrategy CreateLLMStrategy(SplitOptions options, string? model, string? apiKey)
    {
        var chatClient = LlmClientFactory.Create(model, apiKey, logger);
        var llmLogger = serviceProvider.GetService<ILogger<LLMSplitStrategy>>();
        return new LLMSplitStrategy(chatClient, llmLogger);
    }
}
