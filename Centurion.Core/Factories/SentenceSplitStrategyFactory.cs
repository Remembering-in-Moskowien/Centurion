using Centurion.Abstractions.Factories;
using Centurion.Abstractions.Strategy;
using Centurion.Core.Strategy.SentenceSplit;
using Centurion.Models.Llm;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Factories;

/// <summary>
/// 分句策略工厂：按策略名称创建分句策略（规则式或基于大语言模型）。
/// </summary>
public class SentenceSplitStrategyFactory(
    IServiceProvider serviceProvider,
    ILogger<SentenceSplitStrategyFactory> logger)
    : ISentenceSplitStrategyFactory
{
    /// <summary>
    /// 按策略类型创建分句策略。
    /// </summary>
    /// <param name="strategy">分句策略名称：规则式 "rule"/"rule-aggressive"（积极，默认）、"rule-passive"（消极）、"catalyst"/"nlp" 别名，以及 "llm"。</param>
    /// <param name="options">分句所需的规则选项，供规则式或 LLM 策略使用。</param>
    /// <param name="llm">LLM 连接配置（仅 llm 策略需要）；为空时按旧行为回退（API 密钥 → OpenAI，否则 Ollama）。</param>
    /// <returns>对应的分句策略实例。</returns>
    /// <exception cref="NotSupportedException">当策略名称不受支持时抛出。</exception>
    public ISentenceSplitStrategy Create(string strategy, SplitOptions options, LlmOptions? llm = null)
    {
        return strategy.ToLowerInvariant() switch
        {
            "rule" or "rule-aggressive" => serviceProvider.GetRequiredService<AggressiveRuleSplitStrategy>(),
            "rule-passive" => serviceProvider.GetRequiredService<PassiveRuleSplitStrategy>(),
            "catalyst" or "nlp" => serviceProvider.GetRequiredService<AggressiveRuleSplitStrategy>(),
            "llm" => CreateLLMStrategy(options, llm),
            _ => throw new NotSupportedException($"Split strategy '{strategy}' is not supported.")
        };
    }

    private ISentenceSplitStrategy CreateLLMStrategy(SplitOptions options, LlmOptions? llm)
    {
        var chatClient = LlmClientFactory.Create(llm ?? new LlmOptions(), logger);
        var llmLogger = serviceProvider.GetService<ILogger<LLMSplitStrategy>>();
        return new LLMSplitStrategy(chatClient, llmLogger);
    }
}
