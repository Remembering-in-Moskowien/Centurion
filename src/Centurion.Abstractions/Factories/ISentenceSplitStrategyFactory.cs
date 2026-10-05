using Centurion.Abstractions.Strategy;
using Centurion.Models.Llm;

namespace Centurion.Abstractions.Factories;

/// <summary>
/// Factory that creates a sentence-splitting strategy by name (rule-based or LLM-based).
/// </summary>
public interface ISentenceSplitStrategyFactory
{
    /// <summary>
    /// Creates a sentence-splitting strategy for the specified strategy type.
    /// </summary>
    /// <param name="strategy">Strategy name, such as rule, rule-passive, or llm.</param>
    /// <param name="options">Sentence-splitting options.</param>
    /// <param name="llm">LLM connection options, required only for the llm strategy; when null, falls back to OpenAI if an API key is set, otherwise Ollama.</param>
    /// <returns>The sentence-splitting strategy instance.</returns>
    ISentenceSplitStrategy Create(string strategy, SplitOptions options, LlmOptions? llm = null);
}
