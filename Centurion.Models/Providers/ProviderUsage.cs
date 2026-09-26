namespace Centurion.Models.Providers;

/// <summary>
/// 单次 Provider 调用的用量与成本统计。
/// 由 Provider 实现（或 fallback 链）在每次执行后填充，供运行结束汇总输出
/// （token、音频分钟、缓存命中、估算成本）与预算控制使用。
/// </summary>
public sealed record ProviderUsage
{
    /// <summary>实际执行 Provider 的名称。</summary>
    public string ProviderName { get; init; } = string.Empty;

    /// <summary>使用的模型名；未指定时为空。</summary>
    public string? Model { get; init; }

    /// <summary>输入 token 数（LLM/OCR 等 token 计费域；非计费域为 0）。</summary>
    public int TokensIn { get; init; }

    /// <summary>输出 token 数。</summary>
    public int TokensOut { get; init; }

    /// <summary>处理的音频时长（秒；ASR/TTS/VocalSeparation/Diarization 域）。</summary>
    public double AudioSeconds { get; init; }

    /// <summary>缓存命中次数（模型/工具/结果缓存）。</summary>
    public int CacheHits { get; init; }

    /// <summary>本次调用估算成本（美元）。</summary>
    public double EstimatedCostUsd { get; init; }

    /// <summary>调用耗时（毫秒）。</summary>
    public long LatencyMs { get; init; }

    /// <summary>按能力估算成本的辅助：音频域（CostPerAudioMinuteUsd × 分钟）。</summary>
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

    /// <summary>按能力估算成本的辅助：token 域（CostPer1MTokensUsd × 百万 token）。</summary>
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

    /// <summary>两个用量记录的合并（fallback 链多段执行、多次调用聚合）。</summary>
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
/// Provider 执行结果：携带返回值与用量统计。
/// </summary>
/// <param name="Value">执行返回值。</param>
/// <param name="Usage">用量统计。</param>
public sealed record ProviderResult<T>(T Value, ProviderUsage Usage);

/// <summary>
/// Provider 不可用异常：无 API 密钥、本地工具/模型缺失等。
/// fallback 链捕获本异常切换到备用 Provider；调用方也可据此给出可读提示。
/// </summary>
public sealed class ProviderUnavailableException(string message, string providerName)
    : Exception(message)
{
    /// <summary>不可用的 Provider 名称。</summary>
    public string ProviderName { get; } = providerName;
}

/// <summary>
/// Provider 执行失败异常：重试耗尽后由链路抛出（携带最终 Provider 名）。
/// </summary>
public sealed class ProviderExecutionException(string message, string providerName, Exception? inner = null)
    : Exception(message, inner)
{
    /// <summary>失败的 Provider 名称。</summary>
    public string ProviderName { get; } = providerName;
}
