using Centurion.Models;

namespace Centurion.Abstractions.Strategy;

/// <summary>
/// Translation strategy contract: translates sentence text into the target language and populates <see cref="Sentence.TranslatedText"/>
/// without changing sentence timings or word-level details.
/// </summary>
public interface ITranslationStrategy
{
    /// <summary>Display name of the strategy.</summary>
    string StrategyName { get; }

    /// <summary>
    /// Translates source text sentence by sentence or in batches and writes results to <see cref="Sentence.TranslatedText"/>.
    /// </summary>
    /// <param name="sentences">Sentences to translate; timings remain unchanged and translations are populated in place.</param>
    /// <param name="options">Translation options, including target language, glossary, and target-language script.</param>
    /// <param name="cancellationToken">Token used to cancel translation.</param>
    /// <returns>The translated sentences.</returns>
    Task<List<Sentence>> TranslateAsync(
        List<Sentence> sentences,
        TranslationOptions options,
        CancellationToken cancellationToken = default);
}
