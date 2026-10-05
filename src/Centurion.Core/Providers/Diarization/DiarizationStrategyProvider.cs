using Centurion.Abstractions.Providers;
using Centurion.Abstractions.Strategy;
using Centurion.Models.Providers;

namespace Centurion.Core.Providers.Diarization;

/// <summary>
/// Speaker-segmentation provider: adapts <see cref="IDiarizationStrategy"/> (built-in CrispASR /
/// Pyannote+TitaNet). Runs locally with no key required; availability is determined by execution result.
/// </summary>
public sealed class DiarizationStrategyProvider(
    string name,
    string displayName,
    IDiarizationStrategy strategy,
    ProviderCapabilities capabilities) : IDiarizationProvider
{
    private readonly IDiarizationStrategy _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));

    /// <inheritdoc />
    public string Name { get; } = name;

    /// <inheritdoc />
    public string DisplayName { get; } = displayName;

    /// <inheritdoc />
    public ProviderCapabilities Capabilities { get; } = capabilities;

    /// <inheritdoc />
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(true);

    /// <inheritdoc />
    public async Task<ProviderResult<IReadOnlyList<SpeakerSegment>>> DiarizeAsync(
        string audioPath, int numSpeakers, string? segmentModel, CancellationToken cancellationToken)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var segments = await _strategy.DiarizeAsync(audioPath, numSpeakers, segmentModel, cancellationToken,
            Centurion.Models.Workflow.InferenceDevice.Auto);
        sw.Stop();

        var usage = ProviderUsage.ForAudio(DisplayName, segmentModel, 0, 0, 0, sw.ElapsedMilliseconds);
        return new ProviderResult<IReadOnlyList<SpeakerSegment>>(segments, usage);
    }
}

/// <summary>Registration factory for speaker-segmentation providers (fixed names/capabilities).</summary>
public static class DiarizationProviders
{
    /// <summary>Registration name for the built-in CrispASR backend.</summary>
    public const string CrispAsrName = "crispasr";

    /// <summary>Registration name for the Pyannote+TitaNet backend.</summary>
    public const string PyannoteName = "pyannote";

    /// <summary>CrispASR capability declaration: local, energy/xcorr/vad-turns/foxnose methods.</summary>
    public static ProviderCapabilities CrispAsrCapabilities => ProviderCapabilities.Local(
        requiresGpu: false,
        latency: ProviderLatency.Low,
        quality: ProviderQualityLevel.Normal,
        description: "CrispASR built-in speaker segmentation (energy/xcorr/vad-turns/foxnose)");

    /// <summary>Pyannote capability declaration: local, high accuracy.</summary>
    public static ProviderCapabilities PyannoteCapabilities => ProviderCapabilities.Local(
        requiresGpu: true,
        latency: ProviderLatency.High,
        quality: ProviderQualityLevel.High,
        description: "Pyannote segmentation + TitaNet embedding (high accuracy, GPU recommended)");
}
