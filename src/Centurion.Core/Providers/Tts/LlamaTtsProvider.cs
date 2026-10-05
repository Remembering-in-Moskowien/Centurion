using Centurion.Abstractions.Providers;
using Centurion.Abstractions.Tts;
using Centurion.Models.Providers;

namespace Centurion.Core.Providers.Tts;

/// <summary>
/// TTS provider: adapts <see cref="ITtsEngine"/> (llama.cpp llama-tts local synthesis).
/// Keyless, no per-use cost; availability is determined by the execution result.
/// </summary>
public sealed class LlamaTtsProvider(
    string name,
    string displayName,
    ITtsEngine engine,
    ProviderCapabilities capabilities) : ITtsProvider
{
    private readonly ITtsEngine _engine = engine ?? throw new ArgumentNullException(nameof(engine));

    /// <inheritdoc />
    public string Name { get; } = name;

    /// <inheritdoc />
    public string DisplayName { get; } = displayName;

    /// <inheritdoc />
    public ProviderCapabilities Capabilities { get; } = capabilities;

    /// <inheritdoc />
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(true);

    /// <inheritdoc />
    public async Task<ProviderResult<double>> SynthesizeAsync(
        string text, string? referenceAudioPath, string language, string outputWavPath, CancellationToken cancellationToken)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var duration = await _engine.SynthesizeAsync(text, referenceAudioPath, language, outputWavPath, cancellationToken);
        sw.Stop();

        var usage = ProviderUsage.ForAudio(DisplayName, null, duration, 0, 0, sw.ElapsedMilliseconds);
        return new ProviderResult<double>(duration, usage);
    }
}

/// <summary>Registration factory for the llama-tts TTS provider.</summary>
public static class LlamaTtsProviders
{
    /// <summary>Registered name.</summary>
    public const string Name = "llama-tts";

    /// <summary>Capability declaration: local, language-agnostic, high quality (voice cloning).</summary>
    public static ProviderCapabilities Capabilities => ProviderCapabilities.Local(
        requiresGpu: false,
        latency: ProviderLatency.Medium,
        quality: ProviderQualityLevel.High,
        description: "llama.cpp llama-tts (Qwen3-TTS) local synthesis, speaker-reference voice cloning");
}
