namespace Centurion.Models.Providers;

/// <summary>Execution kind: local inference or cloud API.</summary>
public enum ProviderKind
{
    /// <summary>Local inference (whisper.cpp / Ollama / llama.cpp / demucs-rs, etc.); no API key required and no pay-per-use cost.</summary>
    Local,

    /// <summary>Cloud API (OpenAI / Groq / DashScope / Deepgram / Zhipu, etc.); requires an API key and is billed per use.</summary>
    Cloud
}

/// <summary>Latency tier (used for profile selection and cost estimation).</summary>
public enum ProviderLatency
{
    /// <summary>Low latency: small local models or high-performance cloud endpoints.</summary>
    Low,
    /// <summary>Medium latency: general-purpose cloud ASR/LLM.</summary>
    Medium,
    /// <summary>High latency: large models or queue-based endpoints.</summary>
    High
}

/// <summary>Quality tier (used for profile selection).</summary>
public enum ProviderQualityLevel
{
    /// <summary>Draft: fastest but average quality (tiny/base, fast endpoints).</summary>
    Draft,
    /// <summary>Normal: balanced default (small/medium, whisper-1).</summary>
    Normal,
    /// <summary>High quality: large models (large-v3, qwen3-asr-1.7b, glm-4v).</summary>
    High,
    /// <summary>Ultra quality: the largest available models (highest cost).</summary>
    Ultra
}

/// <summary>
/// Provider capability declaration: local/cloud, languages, GPU, cost, latency, quality tier.
/// Used by the provider factory for selection, profile decisions, command display, and cost estimation.
/// </summary>
/// <param name="Kind">Execution kind.</param>
/// <param name="SupportedLanguages">Supported language codes; an empty set means any language.</param>
/// <param name="RequiresGpu">Whether a GPU is required (large local models).</param>
/// <param name="CostPerAudioMinuteUsd">Estimated cost per audio minute in USD for audio processing (ASR/OCR/TTS); 0 for local.</param>
/// <param name="CostPer1MTokensUsd">Estimated cost per million tokens in USD for LLM use; 0 for local.</param>
/// <param name="Latency">Latency tier.</param>
/// <param name="Quality">Quality tier.</param>
/// <param name="Description">Human-readable description.</param>
public sealed record ProviderCapabilities(
    ProviderKind Kind,
    IReadOnlySet<string> SupportedLanguages,
    bool RequiresGpu,
    double CostPerAudioMinuteUsd,
    double CostPer1MTokensUsd,
    ProviderLatency Latency,
    ProviderQualityLevel Quality,
    string Description)
{
    /// <summary>Convenience factory for local provider capabilities (cost is always 0).</summary>
    public static ProviderCapabilities Local(
        bool requiresGpu, ProviderLatency latency, ProviderQualityLevel quality, string description,
        params string[] languages) =>
        new(ProviderKind.Local, new HashSet<string>(languages, StringComparer.OrdinalIgnoreCase),
            requiresGpu, 0, 0, latency, quality, description);

    /// <summary>Convenience factory for cloud provider capabilities.</summary>
    public static ProviderCapabilities Cloud(
        double costPerAudioMinuteUsd, double costPer1MTokensUsd,
        ProviderLatency latency, ProviderQualityLevel quality, string description,
        params string[] languages) =>
        new(ProviderKind.Cloud, new HashSet<string>(languages, StringComparer.OrdinalIgnoreCase),
            false, costPerAudioMinuteUsd, costPer1MTokensUsd, latency, quality, description);

    /// <summary>Whether the specified language is supported (empty set = any language).</summary>
    public bool Supports(string language) =>
        SupportedLanguages.Count == 0 || SupportedLanguages.Contains(language);
}
