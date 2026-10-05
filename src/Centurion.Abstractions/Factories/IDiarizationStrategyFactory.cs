using Centurion.Abstractions.Strategy;

namespace Centurion.Abstractions.Factories;

/// <summary>
/// Factory that creates a diarization strategy by backend name.
/// </summary>
public interface IDiarizationStrategyFactory
{
    /// <summary>
    /// Creates a diarization strategy for the specified backend.
    /// </summary>
    /// <param name="backend">"crispasr" or "pyannote".</param>
    /// <returns>The diarization strategy instance.</returns>
    IDiarizationStrategy Create(string backend);
}
