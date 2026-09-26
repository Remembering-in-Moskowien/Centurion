using Centurion.Abstractions.Providers;
using Centurion.Core.Capabilities.Infrastructure.Asr;
using Centurion.Models;
using Centurion.Models.Asr;
using Centurion.Models.Providers;

namespace Centurion.Core.Providers.Asr;

/// <summary>
/// 云端 ASR Provider：适配 <see cref="CloudAsrStrategy"/>，按提供商（OpenAI/Groq/DashScope/Deepgram）
/// 实例化注册。无 API 密钥时 <see cref="IsAvailableAsync"/> 返回 false，由 fallback 链自动切换本地。
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

    /// <summary>API 密钥；由工厂在创建时经 <see cref="ApiKeyStore"/> 解析注入。</summary>
    public string? ApiKey { get; set; }

    /// <summary>自定义端点；为空按提供商默认。</summary>
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

/// <summary>云端 ASR Provider 的注册工厂（名称/能力/端点默认值固定）。</summary>
public static class CloudAsrProviders
{
    /// <summary>按提供商返回注册名。</summary>
    public static string NameFor(AsrProvider provider) => provider switch
    {
        AsrProvider.OpenAI => "openai",
        AsrProvider.Groq => "groq",
        AsrProvider.DashScope => "dashscope",
        AsrProvider.Deepgram => "deepgram",
        _ => provider.ToString().ToLowerInvariant()
    };

    /// <summary>按提供商返回显示名。</summary>
    public static string DisplayNameFor(AsrProvider provider) => provider switch
    {
        AsrProvider.OpenAI => "OpenAI Whisper",
        AsrProvider.Groq => "Groq Whisper",
        AsrProvider.DashScope => "DashScope Paraformer",
        AsrProvider.Deepgram => "Deepgram Nova",
        _ => provider.ToString()
    };

    /// <summary>按提供商返回能力声明（成本为估算值，供选型与成本输出）。</summary>
    public static ProviderCapabilities CapabilitiesFor(AsrProvider provider) => provider switch
    {
        AsrProvider.OpenAI => ProviderCapabilities.Cloud(0.006, 0, ProviderLatency.Medium, ProviderQualityLevel.High,
            "OpenAI Whisper-1（whisper-1，按音频分钟计费）"),
        AsrProvider.Groq => ProviderCapabilities.Cloud(0, 0, ProviderLatency.Low, ProviderQualityLevel.High,
            "Groq whisper-large-v3（当前免费档，延迟低）"),
        AsrProvider.DashScope => ProviderCapabilities.Cloud(0.0015, 0, ProviderLatency.Medium, ProviderQualityLevel.Normal,
            "阿里百炼 DashScope paraformer-realtime-v2（低成本）"),
        AsrProvider.Deepgram => ProviderCapabilities.Cloud(0.0043, 0, ProviderLatency.Medium, ProviderQualityLevel.High,
            "Deepgram Nova-2（高精度，原生词级时间戳）"),
        _ => ProviderCapabilities.Cloud(0, 0, ProviderLatency.Medium, ProviderQualityLevel.Normal, "Unknown cloud ASR")
    };
}
