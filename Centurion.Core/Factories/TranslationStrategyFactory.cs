using Centurion.Abstractions;
using Centurion.Abstractions.Factories;
using Centurion.Abstractions.Strategy;
using Centurion.Core.Strategy.Translation;
using Centurion.Models.Llm;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Factories;

/// <summary>
/// 翻译策略工厂：按策略名称创建翻译策略，目前支持基于大语言模型（各 OpenAI 兼容服务/Ollama）的 "llm" 策略。
/// </summary>
public class TranslationStrategyFactory(
    IServiceProvider serviceProvider,
    ILogger<TranslationStrategyFactory> logger) : ITranslationStrategyFactory
{
    /// <summary>
    /// 按策略名称创建翻译策略实例。
    /// </summary>
    /// <param name="strategy">策略名称，支持 "llm"。</param>
    /// <param name="llm">LLM 连接配置；为空时按旧行为回退（API 密钥 → OpenAI，否则 Ollama）。</param>
    /// <returns>对应的翻译策略实例。</returns>
    /// <exception cref="NotSupportedException">当策略名称不受支持时抛出。</exception>
    public ITranslationStrategy Create(string strategy, LlmOptions? llm = null)
    {
        return strategy.ToLowerInvariant() switch
        {
            "llm" => new LLMTranslationStrategy(
                LlmClientFactory.Create(llm ?? new LlmOptions(), logger),
                serviceProvider.GetService<ILogger<LLMTranslationStrategy>>()),
            _ => throw new NotSupportedException($"Translation strategy '{strategy}' is not supported.")
        };
    }
}
