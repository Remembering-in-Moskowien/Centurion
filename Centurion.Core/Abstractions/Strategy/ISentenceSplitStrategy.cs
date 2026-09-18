using Centurion.Core.Models;

namespace Centurion.Core.Abstractions.Strategy;

public interface ISentenceSplitStrategy
{
    Task<List<Sentence>> Split(List<Word> words, SplitOptions options);
}
