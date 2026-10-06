using Centurion.Abstractions.Providers;
using Centurion.Abstractions.Strategy;
using Centurion.Models;
using Centurion.Models.Providers;

namespace Centurion.Core.Providers.Asr;

/// <summary>
/// Local ASR provider: adapts the existing local transcription strategies (whisper.cpp /
/// CrispASR-Qwen / CrispASR-Whisper). Runs as local inference with no key and no per-use cost;
/// availability is determined by execution result (the fallback chain switches on failure).
/// </summary>
public sealed class LocalAsrProvider(
    string name,
    string displayName,
    ITranscriptionStrategy strategy,
    ProviderCapabilities capabilities) : IAsrProvider
{
    private readonly ITranscriptionStrategy _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));

    /// <summary>
    /// The transcription strategy backing this provider. Exposed so the pipeline assembler can
    /// read the strategy's declared capabilities (e.g. forced-aligned timestamps) and tailor the
    /// DAG without consulting the strategy object separately.
    /// </summary>
    public ITranscriptionStrategy Strategy => _strategy;

    /// <inheritdoc />
    public string Name { get; } = name;

    /// <inheritdoc />
    public string DisplayName { get; } = displayName;

    /// <inheritdoc />
    public ProviderCapabilities Capabilities { get; } = capabilities;

    /// <inheritdoc />
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(true);

    /// <inheritdoc />
    public async Task<ProviderResult<IReadOnlyList<Word>>> TranscribeAsync(
        string audioPath, string language, string? model, string? initialPrompt, CancellationToken cancellationToken)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var words = await _strategy.TranscribeAsync(
            audioPath,
            language,
            string.IsNullOrWhiteSpace(model) ? "base" : model!,
            initialPrompt,
            cancellationToken,
            Centurion.Models.Workflow.InferenceDevice.Auto);
        sw.Stop();

        var usage = ProviderUsage.ForAudio(DisplayName, model, 0, 0, 0, sw.ElapsedMilliseconds);
        return new ProviderResult<IReadOnlyList<Word>>(words, usage);
    }
}

/// <summary>Registration factory for the whisper.cpp local provider (fixed name/capabilities).</summary>
public static class WhisperCppAsrProvider
{
    /// <summary>The registration name.</summary>
    public const string Name = "whispercpp";

    /// <summary>Capability declaration: local, language-agnostic, low latency, normal-to-high quality.</summary>
    public static ProviderCapabilities Capabilities => ProviderCapabilities.Local(
        requiresGpu: false,
        latency: ProviderLatency.Low,
        quality: ProviderQualityLevel.Normal,
        description: "whisper.cpp local transcription (tiny/base/small/medium/large, GPU-aware variant)");
}

/// <summary>Registration factory for the CrispASR (Qwen3-ASR) local provider.</summary>
public static class CrispAsrQwenProvider
{
    /// <summary>The registration name.</summary>
    public const string Name = "crispasr-qwen";

    /// <summary>Capability declaration: local, language-agnostic, medium-low latency, high quality.</summary>
    public static ProviderCapabilities Capabilities => ProviderCapabilities.Local(
        requiresGpu: false,
        latency: ProviderLatency.Medium,
        quality: ProviderQualityLevel.High,
        description: "Qwen3-ASR (CrispASR backend) local transcription, word-level timestamps, quality first");
}

/// <summary>Registration factory for the CrispASR (Whisper backend) local provider.</summary>
public static class CrispAsrWhisperProvider
{
    /// <summary>The registration name.</summary>
    public const string Name = "crispasr-whisper";

    /// <summary>Capability declaration: local, normal quality.</summary>
    public static ProviderCapabilities Capabilities => ProviderCapabilities.Local(
        requiresGpu: false,
        latency: ProviderLatency.Medium,
        quality: ProviderQualityLevel.Normal,
        description: "CrispASR (Whisper backend) local transcription");
}
