using Centurion.Abstractions.Strategy;

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
    /// <param name="model">可选的模型名称；未提供时按后端默认模型处理。</param>
    /// <param name="apiKey">可选的 API 密钥；提供时使用 OpenAI 后端，否则回退本地 Ollama。</param>
    /// <returns>对应的翻译策略实例。</returns>
    ITranslationStrategy Create(string strategy, string? model = null, string? apiKey = null);
}
