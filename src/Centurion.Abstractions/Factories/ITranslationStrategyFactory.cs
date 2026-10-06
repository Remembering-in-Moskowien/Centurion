using Centurion.Abstractions.Strategy;

namespace Centurion.Abstractions.Factories;

/// <summary>
/// Factory contract for creating a translation strategy by name.
/// </summary>
public interface ITranslationStrategyFactory
{
    /// <summary>
    /// Creates a translation strategy for the specified strategy name.
    /// </summary>
    /// <param name="strategy">Strategy name: "llm" (large language model) or "opus" (local OPUS-MT ONNX).</param>
    /// <param name="options">Request options: LLM connection details, OPUS-MT model pair, beam size; when null, legacy defaults apply.</param>
    /// <returns>The translation strategy instance.</returns>
    ITranslationStrategy Create(string strategy, TranslationRequestOptions? options = null);
}
