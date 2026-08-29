using Centurion.Core.Abstractions.Strategy;
using Centurion.Core.Models;

namespace Centurion.Core.Strategy.Alignment;

/// <summary>
/// A no-op alignment strategy that leaves timestamps unchanged.
/// Used when forced alignment is disabled or unavailable.
/// </summary>
public class NoOpAlignmentStrategy : IAlignmentStrategy
{
    public Task<List<Sentence>> AlignAsync(List<Sentence> sentences, string audioPath, CancellationToken cancellationToken)
    {
        // Simply return the original sentences without modification.
        return Task.FromResult(sentences);
    }
}