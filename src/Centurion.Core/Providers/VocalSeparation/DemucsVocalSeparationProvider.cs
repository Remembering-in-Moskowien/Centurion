using Centurion.Abstractions;
using Centurion.Abstractions.Providers;
using Centurion.Core.Workflow.Strategy.VocalSeparation;
using Centurion.Models.Providers;
using Centurion.Models.Workflow;

namespace Centurion.Core.Providers.VocalSeparation;

/// <summary>
/// Vocal separation provider: drives native htdemucs ONNX Runtime inference (no python, no external
/// CLI) to isolate the vocal track. The in-pipeline <see cref="Workflow.Pipeline.Operators.VocalSeparationOperator"/>
/// is the production path; this provider exposes the same engine through the provider abstraction.
/// </summary>
public sealed class DemucsVocalSeparationProvider(
    string name,
    string displayName,
    HtDemucsOnnxVocalSeparator separator,
    ProviderCapabilities capabilities) : IVocalSeparationProvider
{
    /// <summary>Default separation model.</summary>
    public string DefaultModel { get; set; } = "htdemucs";

    /// <inheritdoc />
    public string Name { get; } = name;

    /// <inheritdoc />
    public string DisplayName { get; } = displayName;

    /// <inheritdoc />
    public ProviderCapabilities Capabilities { get; } = capabilities;

    /// <inheritdoc />
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(true);

    /// <inheritdoc />
    public async Task<ProviderResult<string>> SeparateVocalsAsync(
        string audioPath, string outputWavPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(audioPath))
            throw new FileNotFoundException($"Audio file not found: {audioPath}", audioPath);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await separator.SeparateVocalsAsync(
            audioPath, outputWavPath, DefaultModel, InferenceDevice.Auto, cancellationToken);
        sw.Stop();

        var usage = ProviderUsage.ForAudio(DisplayName, DefaultModel, 0, 0, 0, sw.ElapsedMilliseconds);
        return new ProviderResult<string>(outputWavPath, usage);
    }
}

/// <summary>htdemucs ONNX vocal separation provider registration factory.</summary>
public static class DemucsVocalSeparationProviders
{
    /// <summary>Registered name.</summary>
    public const string Name = "demucs";

    /// <summary>Capability declaration: local, language-agnostic, high-quality separation.</summary>
    public static ProviderCapabilities Capabilities => ProviderCapabilities.Local(
        requiresGpu: false,
        latency: ProviderLatency.High,
        quality: ProviderQualityLevel.High,
        description: "htdemucs (ONNX Runtime) local vocal separation, GPU accelerated via DirectML when available");
}
