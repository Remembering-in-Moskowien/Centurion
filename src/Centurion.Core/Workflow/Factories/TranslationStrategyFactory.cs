using Centurion.Abstractions;
using Centurion.Abstractions.Factories;
using Centurion.Abstractions.Strategy;
using Centurion.Core.Capabilities.Managers.Media;
using Centurion.Core.Workflow.Strategy.Translation;
using Centurion.Models.Llm;
using Centurion.Models.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Workflow.Factories;

/// <summary>
/// Translation strategy factory: creates the translation strategy by strategy name.
/// Supports "llm" (any OpenAI-compatible service / Ollama) and "opus" (local OPUS-MT
/// ONNX models, fully offline after the first-use download).
/// </summary>
public class TranslationStrategyFactory(
    IServiceProvider serviceProvider,
    ILogger<TranslationStrategyFactory> logger) : ITranslationStrategyFactory
{
    /// <summary>
    /// Creates the translation strategy instance for the given strategy name.
    /// </summary>
    /// <param name="strategy">The strategy name: "llm" or "opus".</param>
    /// <param name="options">Request options: LLM connection details for "llm"; OPUS-MT model pair and beam size for "opus".</param>
    /// <returns>The matching translation strategy instance.</returns>
    /// <exception cref="NotSupportedException">Thrown when the strategy name is not supported.</exception>
    public ITranslationStrategy Create(string strategy, TranslationRequestOptions? options = null)
    {
        return strategy.ToLowerInvariant() switch
        {
            "llm" => new LLMTranslationStrategy(
                LlmClientFactory.Create(options?.Llm ?? new LlmOptions(), logger),
                serviceProvider.GetService<ILogger<LLMTranslationStrategy>>()),
            "opus" => CreateOpus(options),
            _ => throw new NotSupportedException($"Translation strategy '{strategy}' is not supported. Available: llm, opus.")
        };
    }

    private ITranslationStrategy CreateOpus(TranslationRequestOptions? options)
    {
        var registry = serviceProvider.GetRequiredService<ModelRegistry>();
        var modelName = OpusMtModelNames.ResolvePair(options?.Model, options?.SourceLanguage, options?.TargetLanguage);

        if (string.IsNullOrWhiteSpace(modelName) || !registry.OpusMtModels.ContainsKey(modelName))
        {
            var available = string.Join(", ",
                registry.OpusMtModels.Keys
                    .Where(k => !k.EndsWith("-fp32", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(k => k, StringComparer.Ordinal));
            throw new NotSupportedException(
                $"OPUS-MT model '{modelName}' is not registered. Available pairs: {available} " +
                "(append -fp32 for the full-precision build). Use --model <pair> or an explicit --source-language/--target-language.");
        }

        var manager = ActivatorUtilities.CreateInstance<ModelManager>(
            serviceProvider, modelName, registry.OpusMtModels, "opusmt");

        return new OpusMtTranslationStrategy(
            manager,
            Math.Max(1, options?.BeamSize ?? 4),
            Math.Max(1, options?.MaxLength ?? 256),
            serviceProvider.GetService<ILogger<OpusMtTranslationStrategy>>());
    }
}
