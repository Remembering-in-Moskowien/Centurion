using Centurion.Abstractions;
using Centurion.Abstractions.Factories;
using Centurion.Abstractions.Strategy;
using Centurion.Core.Workflow.Strategy.Translation;using Centurion.Models.Llm;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Workflow.Factories;

/// <summary>
/// Translation strategy factory: creates the translation strategy by strategy name; currently supports the
/// "llm" strategy backed by a large language model (any OpenAI-compatible service / Ollama).
/// </summary>
public class TranslationStrategyFactory(
    IServiceProvider serviceProvider,
    ILogger<TranslationStrategyFactory> logger) : ITranslationStrategyFactory
{
    /// <summary>
    /// Creates the translation strategy instance for the given strategy name.
    /// </summary>
    /// <param name="strategy">The strategy name; supports "llm".</param>
    /// <param name="llm">LLM connection options; when null the legacy fallback applies (API key present -> OpenAI, otherwise Ollama).</param>
    /// <returns>The matching translation strategy instance.</returns>
    /// <exception cref="NotSupportedException">Thrown when the strategy name is not supported.</exception>
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
