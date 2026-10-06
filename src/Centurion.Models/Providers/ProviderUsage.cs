namespace Centurion.Models.Providers;

/// <summary>
/// Usage and cost statistics for a single provider call.
/// Populated by the provider implementation (or fallback chain) after each execution,
/// for end-of-run summary output (tokens, audio minutes, cache hits, estimated cost) and budget control.
/// </summary>
public sealed record ProviderUsage
{
    /// <summary>Name of the provider that actually executed.</summary>
    public string ProviderName { get; init; } = string.Empty;

    /// <summary>Model name used; empty when not specified.</summary>
    public string? Model { get; init; }

    /// <summary>Number of input tokens (token-billed domains such as LLM/OCR; 0 for non-billed domains).</summary>
    public int TokensIn { get; init; }

    /// <summary>Number of output tokens.</summary>
    public int TokensOut { get; init; }

    /// <summary>Duration of audio processed, in seconds (ASR/TTS/VocalSeparation/Diarization domains).</summary>
    public double AudioSeconds { get; init; }

    /// <summary>Number of cache hits (model/tool/result cache).</summary>
    public int CacheHits { get; init; }

    /// <summary>Estimated cost of this call, in USD.</summary>
    public double EstimatedCostUsd { get; init; }

    /// <summary>Call latency, in milliseconds.</summary>
    public long LatencyMs { get; init; }

    /// <summary>Helper for cost estimation from capabilities: audio domain (CostPerAudioMinuteUsd x minutes).</summary>
    public static ProviderUsage ForAudio(
        string providerName, string? model, double audioSeconds,
        double costPerAudioMinuteUsd, int cacheHits, long latencyMs) =>
        new()
        {
            ProviderName = providerName,
            Model = model,
            AudioSeconds = audioSeconds,
            CacheHits = cacheHits,
            LatencyMs = latencyMs,
            EstimatedCostUsd = costPerAudioMinuteUsd > 0
                ? costPerAudioMinuteUsd * audioSeconds / 60.0
                : 0
        };

    /// <summary>Helper for cost estimation from capabilities: token domain (CostPer1MTokensUsd x million tokens).</summary>
    public static ProviderUsage ForTokens(
        string providerName, string? model, int tokensIn, int tokensOut,
        double costPer1MTokensUsd, int cacheHits, long latencyMs) =>
        new()
        {
            ProviderName = providerName,
            Model = model,
            TokensIn = tokensIn,
            TokensOut = tokensOut,
            CacheHits = cacheHits,
            LatencyMs = latencyMs,
            EstimatedCostUsd = costPer1MTokensUsd > 0
                ? costPer1MTokensUsd * (tokensIn + tokensOut) / 1_000_000.0
                : 0
        };

    /// <summary>Merges two usage records (multi-segment fallback chain execution, aggregation of multiple calls).</summary>
    public static ProviderUsage operator +(ProviderUsage a, ProviderUsage b) => new()
    {
        ProviderName = a.ProviderName == b.ProviderName ? a.ProviderName : $"{a.ProviderName}+{b.ProviderName}",
        Model = a.Model ?? b.Model,
        TokensIn = a.TokensIn + b.TokensIn,
        TokensOut = a.TokensOut + b.TokensOut,
        AudioSeconds = a.AudioSeconds + b.AudioSeconds,
        CacheHits = a.CacheHits + b.CacheHits,
        EstimatedCostUsd = a.EstimatedCostUsd + b.EstimatedCostUsd,
        LatencyMs = a.LatencyMs + b.LatencyMs
    };
}

/// <summary>
/// Provider execution result: carries the return value and usage statistics.
/// </summary>
/// <param name="Value">Execution return value.</param>
/// <param name="Usage">Usage statistics.</param>
public sealed record ProviderResult<T>(T Value, ProviderUsage Usage);

/// <summary>
/// Provider unavailable exception: no API key, missing local tool/model, etc.
/// The fallback chain catches this exception to switch to a backup provider; callers can also use it to show a readable message.
/// </summary>
public sealed class ProviderUnavailableException(string message, string providerName)
    : Exception(message)
{
    /// <summary>Name of the unavailable provider.</summary>
    public string ProviderName { get; } = providerName;
}

/// <summary>
/// Provider execution failure exception: thrown by the chain after retries are exhausted (carries the final provider name).
/// </summary>
public sealed class ProviderExecutionException(string message, string providerName, Exception? inner = null)
    : Exception(message, inner)
{
    /// <summary>Name of the failed provider.</summary>
    public string ProviderName { get; } = providerName;
}
