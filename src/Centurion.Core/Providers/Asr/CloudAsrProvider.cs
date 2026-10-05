using Centurion.Abstractions.Providers;
using Centurion.Core.Capabilities.Infrastructure.Asr;
using Centurion.Models;
using Centurion.Models.Asr;
using Centurion.Models.Providers;

namespace Centurion.Core.Providers.Asr;

/// <summary>
/// Cloud ASR provider: adapts <see cref="CloudAsrStrategy"/>, instantiated and registered per provider
/// (OpenAI/Groq/DashScope/Deepgram). Without an API key, <see cref="IsAvailableAsync"/> returns false and
/// the fallback chain automatically switches to local.
/// </summary>
public sealed class CloudAsrProvider(
    string name,
    string displayName,
    AsrProvider provider,
    CloudAsrStrategy strategy,
    ProviderCapabilities capabilities) : IAsrProvider
{
    private readonly CloudAsrStrategy _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));

    /// <inheritdoc />
    public string Name { get; } = name;

    /// <inheritdoc />
    public string DisplayName { get; } = displayName;

    /// <inheritdoc />
    public ProviderCapabilities Capabilities { get; } = capabilities;

    /// <summary>API key; resolved and injected by the factory at creation via <see cref="ApiKeyStore"/>.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Custom endpoint; falls back to the provider default when empty.</summary>
    public string? BaseUrl { get; set; }

    /// <inheritdoc />
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) =>
        Task.FromResult(!string.IsNullOrWhiteSpace(ApiKey));

    /// <inheritdoc />
    public async Task<ProviderResult<IReadOnlyList<Word>>> TranscribeAsync(
        string audioPath, string language, string? model, string? initialPrompt, CancellationToken cancellationToken)
    {
        _strategy.Provider = provider;
        _strategy.ApiKey = ApiKey;
        _strategy.BaseUrl = BaseUrl;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var words = await _strategy.TranscribeAsync(
            audioPath, language, model ?? string.Empty, initialPrompt, cancellationToken);
        sw.Stop();

        var usage = ProviderUsage.ForAudio(DisplayName, model, 0, Capabilities.CostPerAudioMinuteUsd, 0, sw.ElapsedMilliseconds);
        return new ProviderResult<IReadOnlyList<Word>>(words, usage);
    }
}

/// <summary>Registration factory for cloud ASR providers (fixed names/capabilities/endpoint defaults).</summary>
public static class CloudAsrProviders
{
    /// <summary>Returns the registration name for the given provider.</summary>
    public static string NameFor(AsrProvider provider) => provider switch
    {
        AsrProvider.OpenAI => "openai",
        AsrProvider.Groq => "groq",
        AsrProvider.DashScope => "dashscope",
        AsrProvider.Deepgram => "deepgram",
        _ => provider.ToString().ToLowerInvariant()
    };

    /// <summary>Returns the display name for the given provider.</summary>
    public static string DisplayNameFor(AsrProvider provider) => provider switch
    {
        AsrProvider.OpenAI => "OpenAI Whisper",
        AsrProvider.Groq => "Groq Whisper",
        AsrProvider.DashScope => "DashScope Paraformer",
        AsrProvider.Deepgram => "Deepgram Nova",
        _ => provider.ToString()
    };

    /// <summary>Returns the capability declaration for the given provider (costs are estimates, used for selection and cost reporting).</summary>
    public static ProviderCapabilities CapabilitiesFor(AsrProvider provider) => provider switch
    {
        AsrProvider.OpenAI => ProviderCapabilities.Cloud(0.006, 0, ProviderLatency.Medium, ProviderQualityLevel.High,
            "OpenAI Whisper-1 (whisper-1, billed per audio minute)"),
        AsrProvider.Groq => ProviderCapabilities.Cloud(0, 0, ProviderLatency.Low, ProviderQualityLevel.High,
            "Groq whisper-large-v3 (currently free tier, low latency)"),
        AsrProvider.DashScope => ProviderCapabilities.Cloud(0.0015, 0, ProviderLatency.Medium, ProviderQualityLevel.Normal,
            "Alibaba DashScope paraformer-realtime-v2 (low cost)"),
        AsrProvider.Deepgram => ProviderCapabilities.Cloud(0.0043, 0, ProviderLatency.Medium, ProviderQualityLevel.High,
            "Deepgram Nova-2 (high accuracy, native word-level timestamps)"),
        _ => ProviderCapabilities.Cloud(0, 0, ProviderLatency.Medium, ProviderQualityLevel.Normal, "Unknown cloud ASR")
    };
}
