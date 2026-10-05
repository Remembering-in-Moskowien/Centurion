using Centurion.Abstractions;
using Centurion.Core.Workflow.Factories;
using Centurion.Abstractions.Providers;
using Centurion.Core.Capabilities.Managers.Runtime;
using Centurion.Core.Workflow.Pipeline.Operators;
using Centurion.Models.Providers;

namespace Centurion.Core.Providers.VocalSeparation;

/// <summary>
/// Vocal separation provider: directly drives the demucs-rs CLI (-s vocals) to isolate the vocal track.
/// Lightweight path: the tool is downloaded on demand, model name defaults to htdemucs (cached by demucs-rs itself);
/// the in-pipeline <see cref="VocalSeparationOperator"/> is the production path (with mirror pre-download enhancement).
/// </summary>
public sealed class DemucsVocalSeparationProvider(
    string name,
    string displayName,
    IToolManagerFactory toolFactory,
    ProcessManager processManager,
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
        var tool = toolFactory.Create("demucsrs", Centurion.Models.Workflow.InferenceDevice.Auto);
        await tool.EnsureToolAsync(cancellationToken);

        var outputDir = Path.Combine(Path.GetDirectoryName(outputWavPath) ?? Path.GetTempPath(),
            $"vocalsep_provider_{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputDir);

        var args = VocalSeparationOperator.BuildArguments(DefaultModel, audioPath, outputDir);
        await processManager.ExecuteAsync(tool.ExecutablePath, args, cancellationToken);

        var vocalsFile = VocalSeparationOperator.FindVocalsFile(outputDir);
        if (vocalsFile is null)
            throw new ProviderExecutionException("Demucs finished but no 'vocals' stem was found.", Name);

        if (!string.Equals(Path.GetFullPath(vocalsFile), Path.GetFullPath(outputWavPath), StringComparison.OrdinalIgnoreCase))
            File.Copy(vocalsFile, outputWavPath, overwrite: true);

        sw.Stop();
        var usage = ProviderUsage.ForAudio(DisplayName, DefaultModel, 0, 0, 0, sw.ElapsedMilliseconds);
        return new ProviderResult<string>(outputWavPath, usage);
    }
}

/// <summary>Demucs vocal separation provider registration factory.</summary>
public static class DemucsVocalSeparationProviders
{
    /// <summary>Registered name.</summary>
    public const string Name = "demucs";

    /// <summary>Capability declaration: local, language-agnostic, high-quality separation.</summary>
    public static ProviderCapabilities Capabilities => ProviderCapabilities.Local(
        requiresGpu: true,
        latency: ProviderLatency.High,
        quality: ProviderQualityLevel.High,
        description: "demucs-rs (htdemucs) local vocal separation, GPU recommended");
}
