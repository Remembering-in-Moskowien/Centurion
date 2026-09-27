using System.Collections.ObjectModel;
using Centurion.Models.Providers;

namespace Centurion.Abstractions.Providers;

/// <summary>Provider 选型 profile：决定主备偏好、是否允许云端、成本上限取向。</summary>
public enum ProviderProfile
{
    /// <summary>完全离线：仅本地 Provider，禁用云端（无密钥环境默认）。</summary>
    Offline,

    /// <summary>速度优先：低延迟优先（本地小模型或快速云端端点），允许云端。</summary>
    Fast,

    /// <summary>质量优先：高质量模型优先（本地大模型或高质量云端端点），允许云端。</summary>
    Quality,

    /// <summary>成本优先：优先免费/最低成本（本地优先，云端仅低成本端点）。</summary>
    Cheap
}

/// <summary>Provider 横切策略配置：重试、超时、限流、熔断、预算。</summary>
public sealed record ProviderPolicyOptions
{
    /// <summary>单 Provider 最大重试次数（默认 2，即最多执行 3 次）。</summary>
    public int MaxRetries { get; init; } = 2;

    /// <summary>单次调用超时（秒，默认 300）。</summary>
    public int TimeoutSeconds { get; init; } = 300;

    /// <summary>重试间隔基数（毫秒，默认 500，指数退避）。</summary>
    public int RetryBaseDelayMs { get; init; } = 500;

    /// <summary>限流：每分钟最大调用次数（0 = 不限）。</summary>
    public int MaxCallsPerMinute { get; init; } = 0;

    /// <summary>熔断：连续失败达到该次数后熔断（0 = 不熔断）。</summary>
    public int CircuitBreakerFailureThreshold { get; init; } = 3;

    /// <summary>熔断冷却时间（秒，默认 60）。</summary>
    public int CircuitBreakerCooldownSeconds { get; init; } = 60;

    /// <summary>单次运行预算上限（美元，0 = 不限）；超出后链路拒绝继续调用云端。</summary>
    public double BudgetUsdPerRun { get; init; } = 0;

    /// <summary>按 profile 返回默认策略：offline 本地优先、quality 重试更多、fast 超时更短。</summary>
    public static ProviderPolicyOptions ForProfile(ProviderProfile profile) => profile switch
    {
        ProviderProfile.Offline => new ProviderPolicyOptions { TimeoutSeconds = 600 },
        ProviderProfile.Fast => new ProviderPolicyOptions { MaxRetries = 1, TimeoutSeconds = 120 },
        ProviderProfile.Quality => new ProviderPolicyOptions { MaxRetries = 3, TimeoutSeconds = 600, CircuitBreakerFailureThreshold = 5 },
        _ => new ProviderPolicyOptions()
    };
}

/// <summary>
/// Provider 注册表：登记全部已装配 Provider，支持按名称/能力查询。
/// 命令（providers list）与工厂（fallback 链）共用。
/// </summary>
public interface IProviderRegistry
{
    /// <summary>全部已注册 Provider（只读快照）。</summary>
    IReadOnlyList<IProvider> All { get; }

    /// <summary>按名称查找 Provider。</summary>
    IProvider? Find(string name);

    /// <summary>按能力域筛选（域 = 接口类型名，如 "IAsrProvider"）。</summary>
    IReadOnlyList<IProvider> ForDomain(string domain);

    /// <summary>按执行形态筛选。</summary>
    IReadOnlyList<IProvider> OfKind(ProviderKind kind);
}

/// <summary>
/// Provider 工厂：按配置创建单 Provider 与 fallback 链。
/// 命令层/算子层经本工厂解析"主 Provider + 备用 Provider"，实现本地/云互备。
/// </summary>
public interface IProviderFactory
{
    /// <summary>
    /// 解析 ASR fallback 链（主 + 备用，按 profile 与配置决定顺序）。
    /// 例如：配置 OpenAI（有密钥）→ [openai, whispercpp]；无密钥 → [whispercpp, openai]（本地兜底）。
    /// </summary>
    IReadOnlyList<IAsrProvider> CreateAsrChain(
        string engine,
        string? model,
        Centurion.Abstractions.Strategy.AsrOptions? options,
        ProviderProfile profile);

    /// <summary>按后端名创建 OCR Provider。</summary>
    IOcrProvider CreateOcrProvider(string backend, string? model, string? apiKey, string? baseUrl);

    /// <summary>按提供商名创建 LLM Provider。</summary>
    ILlmProvider CreateLlmProvider(
        string provider,
        string? model,
        string? apiKey,
        string? baseUrl);

    /// <summary>当前生效的横切策略配置。</summary>
    ProviderPolicyOptions Policies { get; }
}
