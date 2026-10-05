using Centurion.Abstractions.Factories;
using Centurion.Abstractions.Strategy;
using Centurion.Core.Workflow.Strategy.SentenceSplit;using Centurion.Models.Llm;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Workflow.Factories;

/// <summary>
/// Sentence-split strategy factory: creates the sentence-split strategy by strategy name (rule-based or LLM-based).
/// </summary>
public class SentenceSplitStrategyFactory(
    IServiceProvider serviceProvider,
    ILogger<SentenceSplitStrategyFactory> logger)
    : ISentenceSplitStrategyFactory
{
    /// <summary>
    /// Creates the sentence-split strategy by strategy type.
    /// </summary>
    /// <param name="strategy">The split strategy name: rule-based "rule"/"rule-aggressive" (aggressive, the default), "rule-passive" (passive), the "catalyst"/"nlp" aliases, and "llm".</param>
    /// <param name="options">The rule options required for splitting, used by the rule-based or LLM strategy.</param>
    /// <param name="llm">LLM connection options (only needed for the llm strategy); when null the legacy fallback applies (API key present -> OpenAI, otherwise Ollama).</param>
    /// <returns>The matching sentence-split strategy instance.</returns>
    /// <exception cref="NotSupportedException">Thrown when the strategy name is not supported.</exception>
    public ISentenceSplitStrategy Create(string strategy, SplitOptions options, LlmOptions? llm = null)
    {
        return strategy.ToLowerInvariant() switch
        {
            "rule" or "rule-aggressive" or "aggressive" => serviceProvider.GetRequiredService<AggressiveRuleSplitStrategy>(),
            "rule-passive" or "passive" => serviceProvider.GetRequiredService<PassiveRuleSplitStrategy>(),
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
