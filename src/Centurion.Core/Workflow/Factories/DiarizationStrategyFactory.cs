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
    /// <param name="backend">The diarization backend name; supports "polyvoice" or "wespeaker".</param>
    /// <returns>The diarization strategy instance for the given backend.</returns>
    /// <exception cref="NotSupportedException">Thrown when the backend name is not supported.</exception>
    public IDiarizationStrategy Create(string backend)
    {
        return backend.ToLowerInvariant() switch
        {
            "polyvoice" => serviceProvider.GetRequiredService<PolyVoiceDiarizationStrategy>(),
            "wespeaker" => serviceProvider.GetRequiredService<WeSpeakerDiarizationStrategy>(),
            _ => throw new NotSupportedException($"Diarization backend '{backend}' is not supported. Use 'polyvoice' or 'wespeaker'.")
        };
    }
}
