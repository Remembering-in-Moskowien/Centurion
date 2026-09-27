using Centurion.Abstractions.Strategy;
using Centurion.Models.Llm;

namespace Centurion.Abstractions.Factories;

/// <summary>
/// 分句策略工厂：按策略名称创建分句策略（规则式/LLM 式）。
/// </summary>
public interface ISentenceSplitStrategyFactory
{
    /// <summary>
    /// 按策略类型创建分句策略。
    /// </summary>
    /// <param name="strategy">策略名称（如 rule, rule-passive, llm）</param>
    /// <param name="options">分句配置参数</param>
    /// <param name="llm">LLM 连接配置（仅 llm 策略需要）；为空时按旧行为（API 密钥 → OpenAI，否则 Ollama）回退。</param>
    /// <returns>对应的分句策略实例。</returns>
    ISentenceSplitStrategy Create(string strategy, SplitOptions options, LlmOptions? llm = null);
}
