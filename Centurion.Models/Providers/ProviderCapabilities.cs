namespace Centurion.Models.Providers;

/// <summary>执行形态：本地推理或云端 API。</summary>
public enum ProviderKind
{
    /// <summary>本地推理（whisper.cpp / Ollama / llama.cpp / demucs-rs 等），免 API 密钥，无按量成本。</summary>
    Local,

    /// <summary>云端 API（OpenAI / Groq / DashScope / Deepgram / 智谱 等），需 API 密钥，按量计费。</summary>
    Cloud
}

/// <summary>延迟档位（用于 profile 选型与成本估算）。</summary>
public enum ProviderLatency
{
    /// <summary>低延迟：本地小模型或高性能云端点。</summary>
    Low,
    /// <summary>中等延迟：通用云 ASR/LLM。</summary>
    Medium,
    /// <summary>高延迟：大模型或排队型端点。</summary>
    High
}

/// <summary>质量档位（用于 profile 选型）。</summary>
public enum ProviderQualityLevel
{
    /// <summary>草稿级：最快但质量一般（tiny/base、快速端点）。</summary>
    Draft,
    /// <summary>常规级：默认平衡（small/medium、whisper-1）。</summary>
    Normal,
    /// <summary>高质量：大模型（large-v3、qwen3-asr-1.7b、glm-4v）。</summary>
    High,
    /// <summary>极致质量：最大可用模型（成本最高）。</summary>
    Ultra
}

/// <summary>
/// Provider 能力声明：本地/云、语言、GPU、成本、延迟、质量档位。
/// 供 Provider 工厂选型、profile 决策、命令展示与成本估算使用。
/// </summary>
/// <param name="Kind">执行形态。</param>
/// <param name="SupportedLanguages">支持的语言代码集合；空集合表示不限语言。</param>
/// <param name="RequiresGpu">是否强制要求 GPU（本地大模型）。</param>
/// <param name="CostPerAudioMinuteUsd">音频处理类（ASR/OCR/TTS）每分钟估算成本（美元）；本地为 0。</param>
/// <param name="CostPer1MTokensUsd">LLM 类每百万 token 估算成本（美元）；本地为 0。</param>
/// <param name="Latency">延迟档位。</param>
/// <param name="Quality">质量档位。</param>
/// <param name="Description">人类可读描述。</param>
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
    /// <summary>构造本地 Provider 能力的快捷方式（成本恒为 0）。</summary>
    public static ProviderCapabilities Local(
        bool requiresGpu, ProviderLatency latency, ProviderQualityLevel quality, string description,
        params string[] languages) =>
        new(ProviderKind.Local, new HashSet<string>(languages, StringComparer.OrdinalIgnoreCase),
            requiresGpu, 0, 0, latency, quality, description);

    /// <summary>构造云端 Provider 能力的快捷方式。</summary>
    public static ProviderCapabilities Cloud(
        double costPerAudioMinuteUsd, double costPer1MTokensUsd,
        ProviderLatency latency, ProviderQualityLevel quality, string description,
        params string[] languages) =>
        new(ProviderKind.Cloud, new HashSet<string>(languages, StringComparer.OrdinalIgnoreCase),
            false, costPerAudioMinuteUsd, costPer1MTokensUsd, latency, quality, description);

    /// <summary>是否支持指定语言（空集合 = 不限）。</summary>
    public bool Supports(string language) =>
        SupportedLanguages.Count == 0 || SupportedLanguages.Contains(language);
}
