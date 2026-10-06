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
    /// <summary>Registration name for the polyvoice (Rust CPU) backend.</summary>
    public const string PolyVoiceName = "polyvoice";

    /// <summary>Registration name for the sherpa-onnx WeSpeaker backend.</summary>
    public const string WeSpeakerName = "wespeaker";

    /// <summary>PolyVoice capability declaration: local CPU diarization (powerset + WeSpeaker ResNet34 + AHC).</summary>
    public static ProviderCapabilities PolyVoiceCapabilities => ProviderCapabilities.Local(
        requiresGpu: false,
        latency: ProviderLatency.Low,
        quality: ProviderQualityLevel.High,
        description: "polyvoice (Rust CPU): powerset segmentation + WeSpeaker ResNet34 embeddings + AHC clustering");

    /// <summary>WeSpeaker capability declaration: local, high accuracy (pyannote segmentation + WeSpeaker embeddings).</summary>
    public static ProviderCapabilities WeSpeakerCapabilities => ProviderCapabilities.Local(
        requiresGpu: false,
        latency: ProviderLatency.Medium,
        quality: ProviderQualityLevel.High,
        description: "sherpa-onnx offline diarization (pyannote segmentation + WeSpeaker ResNet34 embeddings)");
}
