using Centurion.Abstractions.Factories;
using Centurion.Abstractions.Strategy;
using Centurion.Core.Capabilities.Managers.Media;
using Centurion.Core.Workflow.Strategy.SentenceSplit;
using Centurion.Models.Llm;
using Centurion.Models.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Workflow.Factories;

/// <summary>
/// Sentence-split strategy factory: creates the sentence-split strategy by strategy name
/// (rule-based, LLM-based, or the local SaT model).
/// </summary>
public class SentenceSplitStrategyFactory(
    IServiceProvider serviceProvider,
    ModelRegistry modelRegistry,
    ILogger<SentenceSplitStrategyFactory> logger)
    : ISentenceSplitStrategyFactory
{
    /// <summary>
    /// Creates the sentence-split strategy by strategy type.
    /// </summary>
    /// <param name="strategy">The split strategy name: rule-based "rule"/"rule-aggressive" (aggressive, the default), "rule-passive" (passive), the "catalyst"/"nlp" aliases, "llm", and "sat"/"wtpsplit" (local SaT model).</param>
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
            "sat" or "wtpsplit" => CreateSatStrategy(options),
            _ => throw new NotSupportedException($"Split strategy '{strategy}' is not supported.")
        };
    }

    private ISentenceSplitStrategy CreateSatStrategy(SplitOptions options)
    {
        var modelName = string.IsNullOrWhiteSpace(options.ModelName) ? "sat-3l-sm" : options.ModelName.Trim().ToLowerInvariant();
        if (!modelRegistry.SatModels.TryGetValue(modelName, out _))
            throw new NotSupportedException($"SaT model '{modelName}' is not registered. Available: {string.Join(", ", modelRegistry.SatModels.Keys)}.");

        var manager = ActivatorUtilities.CreateInstance<ModelManager>(
            serviceProvider, modelName, modelRegistry.SatModels, "sat");
        return new SaTSplitStrategy(manager, options.Threshold);
    }

    private ISentenceSplitStrategy CreateLLMStrategy(SplitOptions options, LlmOptions? llm)
    {
        var chatClient = LlmClientFactory.Create(llm ?? new LlmOptions(), logger);
        var llmLogger = serviceProvider.GetService<ILogger<LLMSplitStrategy>>();
        return new LLMSplitStrategy(chatClient, llmLogger);
    }
}
