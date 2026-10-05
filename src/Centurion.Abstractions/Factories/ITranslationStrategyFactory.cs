using Centurion.Abstractions.Strategy;
using Centurion.Models.Llm;

namespace Centurion.Abstractions.Factories;

/// <summary>
/// Factory contract for creating a translation strategy by name.
/// </summary>
public interface ITranslationStrategyFactory
{
    /// <summary>
    /// Creates a translation strategy for the specified strategy name.
    /// </summary>
    /// <param name="strategy">Strategy name, such as "llm".</param>
    /// <param name="llm">LLM connection options; when null, falls back to OpenAI if an API key is set, otherwise Ollama.</param>
    /// <returns>The translation strategy instance.</returns>
    ITranslationStrategy Create(string strategy, LlmOptions? llm = null);
}
