using Centurion.Models;

namespace Centurion.Abstractions.Strategy;

/// <summary>
/// Sentence-splitting strategy that groups word-level timestamps into display-ready sentences.
/// </summary>
public interface ISentenceSplitStrategy : IPipelineStrategy
{
    /// <summary>
    /// Splits a word list into sentences using the specified options.
    /// </summary>
    /// <param name="words">Words with start and end timestamps.</param>
    /// <param name="options">Sentence-splitting options.</param>
    /// <returns>The resulting sentences.</returns>
    Task<List<Sentence>> Split(List<Word> words, SplitOptions options);
}
