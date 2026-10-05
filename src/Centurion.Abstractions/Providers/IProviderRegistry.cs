using System.Collections.ObjectModel;
using Centurion.Models.Providers;

namespace Centurion.Abstractions.Providers;

/// <summary>Provider selection profile that controls primary/fallback preferences, cloud access, and cost priorities.</summary>
public enum ProviderProfile
{
    /// <summary>Fully offline: local providers only, with cloud access disabled. Default when no API keys are configured.</summary>
    Offline,

    /// <summary>Speed first: prefer low latency, using a small local model or fast cloud endpoint; cloud access is allowed.</summary>
    Fast,

    /// <summary>Quality first: prefer high-quality models, local or cloud; cloud access is allowed.</summary>
    Quality,

    /// <summary>Cost first: prefer free or lowest-cost providers, favoring local providers and low-cost cloud endpoints.</summary>
    Cheap
}

/// <summary>Cross-cutting provider policies for retries, timeouts, rate limits, circuit breakers, and budgets.</summary>
public sealed record ProviderPolicyOptions
{
    /// <summary>Maximum retries per provider (default 2, for up to 3 attempts).</summary>
    public int MaxRetries { get; init; } = 2;

    /// <summary>Timeout per call in seconds (default 300).</summary>
    public int TimeoutSeconds { get; init; } = 300;

    /// <summary>Retry delay base in milliseconds (default 500, with exponential backoff).</summary>
    public int RetryBaseDelayMs { get; init; } = 500;

    /// <summary>Rate limit in calls per minute; 0 means unlimited.</summary>
    public int MaxCallsPerMinute { get; init; } = 0;

    /// <summary>Circuit breaker threshold; 0 disables the circuit breaker.</summary>
    public int CircuitBreakerFailureThreshold { get; init; } = 3;

    /// <summary>Circuit breaker cooldown in seconds (default 60).</summary>
    public int CircuitBreakerCooldownSeconds { get; init; } = 60;

    /// <summary>Per-run budget limit in USD; 0 means unlimited. Cloud calls are rejected after the limit is exceeded.</summary>
    public double BudgetUsdPerRun { get; init; } = 0;

    /// <summary>Returns policies for a profile: offline favors local providers, quality retries more, and fast uses shorter timeouts.</summary>
    public static ProviderPolicyOptions ForProfile(ProviderProfile profile) => profile switch
    {
        ProviderProfile.Offline => new ProviderPolicyOptions { TimeoutSeconds = 600 },
        ProviderProfile.Fast => new ProviderPolicyOptions { MaxRetries = 1, TimeoutSeconds = 120 },
        ProviderProfile.Quality => new ProviderPolicyOptions { MaxRetries = 3, TimeoutSeconds = 600, CircuitBreakerFailureThreshold = 5 },
        _ => new ProviderPolicyOptions()
    };
}

/// <summary>
/// Registry of all configured providers, searchable by name or capability.
/// Shared by commands such as providers list and provider factories that build fallback chains.
/// </summary>
public interface IProviderRegistry
{
    /// <summary>Read-only snapshot of all registered providers.</summary>
    IReadOnlyList<IProvider> All { get; }

    /// <summary>Finds a provider by name.</summary>
    IProvider? Find(string name);

    /// <summary>Filters by capability domain, which is the interface type name, such as "IAsrProvider".</summary>
    IReadOnlyList<IProvider> ForDomain(string domain);

    /// <summary>Filters by execution kind.</summary>
    IReadOnlyList<IProvider> OfKind(ProviderKind kind);
}

/// <summary>
/// Creates individual providers and fallback chains from configuration.
/// Commands and operators use this factory to resolve a primary provider and fallbacks for local/cloud failover.
/// </summary>
public interface IProviderFactory
{
    /// <summary>
    /// Resolves an ASR fallback chain, ordered by profile and configuration.
    /// For example, configured OpenAI with a key yields [openai, whispercpp]; without a key, it yields [whispercpp, openai].
    /// </summary>
    IReadOnlyList<IAsrProvider> CreateAsrChain(
        string engine,
        string? model,
        Centurion.Abstractions.Strategy.AsrOptions? options,
        ProviderProfile profile);

    /// <summary>Creates an OCR provider by backend name.</summary>
    IOcrProvider CreateOcrProvider(string backend, string? model, string? apiKey, string? baseUrl);

    /// <summary>Creates an LLM provider by provider name.</summary>
    ILlmProvider CreateLlmProvider(
        string provider,
        string? model,
        string? apiKey,
        string? baseUrl);

    /// <summary>Currently active cross-cutting policies.</summary>
    ProviderPolicyOptions Policies { get; }
}
