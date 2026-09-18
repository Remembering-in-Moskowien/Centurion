using Centurion.Models;

namespace Centurion.Abstractions.Strategy;


/// <summary>
/// Strategy interface for forced alignment.
/// Implementations align word-level timestamps within sentences using various backends.
/// </summary>
public interface IAlignmentStrategy
{
    /// <summary>
    /// Aligns the given sentences against the audio file, updating word timestamps in-place.
    /// </summary>
    /// <param name="sentences">Sentences containing words with rough timestamps (to be refined).</param>
    /// <param name="audioPath">Path to the audio file (16kHz mono PCM recommended).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Aligned sentences (the same instances are updated in-place).</returns>
    Task<List<Sentence>> AlignAsync(List<Sentence> sentences, string audioPath, CancellationToken cancellationToken);
}