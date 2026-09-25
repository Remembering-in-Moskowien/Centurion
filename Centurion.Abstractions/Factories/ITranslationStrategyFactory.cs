using Centurion.Abstractions.Strategy;
using Centurion.Models.Llm;

namespace Centurion.Abstractions.Factories;

/// <summary>
/// 翻译策略工厂契约：按策略名称创建对应的翻译策略实例。
/// </summary>
public interface ITranslationStrategyFactory
{
    /// <summary>
    /// 按策略名称创建翻译策略。
    /// </summary>
    /// <param name="strategy">策略名称（如 "llm"）。</param>
    /// <param name="llm">LLM 连接配置；为空时按旧行为（API 密钥 → OpenAI，否则 Ollama）回退。</param>
    /// <returns>对应的翻译策略实例。</returns>
    ITranslationStrategy Create(string strategy, LlmOptions? llm = null);
}
