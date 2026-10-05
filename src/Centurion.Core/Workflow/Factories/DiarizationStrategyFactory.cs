using Centurion.Abstractions.Factories;
using Centurion.Abstractions.Strategy;
using Centurion.Core.Workflow.Strategy.Diarization;using Microsoft.Extensions.DependencyInjection;

namespace Centurion.Core.Workflow.Factories;

/// <summary>
/// Speaker diarization strategy factory: dispatches to the implementation matching the backend name.
/// </summary>
public class DiarizationStrategyFactory(IServiceProvider serviceProvider) : IDiarizationStrategyFactory
{
    /// <summary>
    /// Creates the speaker-diarization strategy for the given backend name.
    /// </summary>
    /// <param name="backend">The diarization backend name; supports "crispasr" or "pyannote".</param>
    /// <returns>The diarization strategy instance for the given backend.</returns>
    /// <exception cref="NotSupportedException">Thrown when the backend name is not supported.</exception>
    public IDiarizationStrategy Create(string backend)
    {
        return backend.ToLowerInvariant() switch
        {
            "crispasr" => serviceProvider.GetRequiredService<CrispAsrDiarizationStrategy>(),
            "pyannote" => serviceProvider.GetRequiredService<PyannoteTitaNetDiarizationStrategy>(),
            _ => throw new NotSupportedException($"Diarization backend '{backend}' is not supported. Use 'crispasr' or 'pyannote'.")
        };
    }
}
