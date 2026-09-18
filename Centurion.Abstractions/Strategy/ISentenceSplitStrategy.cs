using Centurion.Models;

namespace Centurion.Abstractions.Strategy;

public interface ISentenceSplitStrategy
{
    Task<List<Sentence>> Split(List<Word> words, SplitOptions options);
}
