using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Centurion.Abstractions.Providers;
using Centurion.Core.Capabilities.Infrastructure.Llm;
using Centurion.Models.Llm;
using Microsoft.Extensions.Logging;
using Centurion.Models.Providers;

namespace Centurion.Core.Providers.Llm;

/// <summary>
/// LLM Provider：统一 OpenAI 兼容 chat/completions 调用。
/// 云端提供商（OpenAI/Groq/DashScope/DeepSeek 等）走注册表默认端点；本地 Ollama 免密钥。
/// token 数与成本为估算值，供统计输出与预算控制。
/// </summary>
public sealed class OpenAiCompatibleLlmProvider(
    string name,
    string displayName,
    LlmProvider provider,
    ProviderCapabilities capabilities,
    ILogger<OpenAiCompatibleLlmProvider> logger) : ILlmProvider
{
    /// <summary>共享 HttpClient（标准单例模式；调用级超时经 linked CTS 控制）。</summary>
    private static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromSeconds(TimeoutSeconds) };
    private const int TimeoutSeconds = 300;

    /// <inheritdoc />
    public string Name { get; } = name;

    /// <inheritdoc />
    public string DisplayName { get; } = displayName;

    /// <summary>API 密钥；本地 Ollama 可空。</summary>
    public string? ApiKey { get; set; }

    /// <summary>自定义端点；为空用注册表默认。</summary>
    public string? BaseUrl { get; set; }

    /// <summary>默认模型；为空用注册表默认。</summary>
    public string? DefaultModel { get; set; }

    /// <inheritdoc />
    public ProviderCapabilities Capabilities { get; } = capabilities;

    /// <inheritdoc />
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
    {
        // 本地 Ollama 无需密钥；云端需密钥。
        if (Capabilities.Kind == ProviderKind.Cloud && string.IsNullOrWhiteSpace(ApiKey))
            return Task.FromResult(false);
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public async Task<ProviderResult<string>> CompleteAsync(
        IReadOnlyList<ChatMessage> messages, string? model, CancellationToken cancellationToken)
    {
        var baseUrl = BaseUrl ?? LlmProviderRegistry.GetDefaultBaseUrl(provider);
        var resolvedModel = model ?? DefaultModel ?? LlmProviderRegistry.GetDefaultModel(provider);
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new ProviderUnavailableException($"LLM provider '{DisplayName}' has no base URL.", Name);

        var endpoint = baseUrl.TrimEnd('/') + "/chat/completions";
        var payload = new JsonObject
        {
            ["model"] = resolvedModel ?? string.Empty,
            ["messages"] = new JsonArray(messages.Select(m => new JsonObject
            {
                ["role"] = m.Role,
                ["content"] = m.Content
            }).ToArray())
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        if (!string.IsNullOrWhiteSpace(ApiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
        request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));
        using var response = await SharedHttp.SendAsync(request, timeoutCts.Token);
        var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);
        sw.Stop();

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("LLM request to {Provider} failed ({Status}): {Body}",
                DisplayName, response.StatusCode, Truncate(body, 300));
            throw new ProviderExecutionException(
                $"LLM request failed with status {(int)response.StatusCode}: {Truncate(body, 300)}", Name);
        }

        var (content, tokensIn, tokensOut) = ParseResponse(body);
        var usage = ProviderUsage.ForTokens(DisplayName, resolvedModel, tokensIn, tokensOut,
            Capabilities.CostPer1MTokensUsd, 0, sw.ElapsedMilliseconds);
        return new ProviderResult<string>(content, usage);
    }

    private static (string Content, int TokensIn, int TokensOut) ParseResponse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var content = root.TryGetProperty("choices", out var choices)
                      && choices.GetArrayLength() > 0
                      && choices[0].TryGetProperty("message", out var message)
                      && message.TryGetProperty("content", out var c)
            ? c.GetString() ?? string.Empty
            : string.Empty;

        var tokensIn = 0;
        var tokensOut = 0;
        if (root.TryGetProperty("usage", out var usage))
        {
            if (usage.TryGetProperty("prompt_tokens", out var pi))
                tokensIn = pi.GetInt32();
            if (usage.TryGetProperty("completion_tokens", out var co))
                tokensOut = co.GetInt32();
        }

        return (content, tokensIn, tokensOut);
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}

/// <summary>LLM Provider 注册工厂（名称/能力映射）。</summary>
public static class LlmProviders
{
    /// <summary>提供商 → 注册名。</summary>
    public static string NameFor(LlmProvider provider) => provider switch
    {
        LlmProvider.OpenAI => "openai",
        LlmProvider.DeepSeek => "deepseek",
        LlmProvider.Moonshot => "moonshot",
        LlmProvider.Zhipu => "zhipu",
        LlmProvider.OpenRouter => "openrouter",
        LlmProvider.Groq => "groq",
        LlmProvider.SiliconFlow => "siliconflow",
        LlmProvider.DashScope => "dashscope",
        LlmProvider.Ark => "ark",
        LlmProvider.Azure => "azure",
        LlmProvider.Ollama => "ollama-llm",
        _ => provider.ToString().ToLowerInvariant()
    };

    /// <summary>提供商 → 能力声明（成本为估算值）。</summary>
    public static ProviderCapabilities CapabilitiesFor(LlmProvider provider) => provider switch
    {
        LlmProvider.Ollama => ProviderCapabilities.Local(false, ProviderLatency.Low, ProviderQualityLevel.Normal,
            "本地 Ollama（llama3.2 等，免密钥）"),
        LlmProvider.OpenAI => ProviderCapabilities.Cloud(0, 1.5, ProviderLatency.Medium, ProviderQualityLevel.High,
            "OpenAI（gpt-4o-mini，按 token 计费）"),
        LlmProvider.DeepSeek => ProviderCapabilities.Cloud(0, 0.6, ProviderLatency.Medium, ProviderQualityLevel.High,
            "DeepSeek（deepseek-chat，低成本高质量）"),
        LlmProvider.Moonshot => ProviderCapabilities.Cloud(0, 1.2, ProviderLatency.Medium, ProviderQualityLevel.Normal,
            "Moonshot Kimi（moonshot-v1-8k）"),
        LlmProvider.Zhipu => ProviderCapabilities.Cloud(0, 0.6, ProviderLatency.Medium, ProviderQualityLevel.Normal,
            "智谱 GLM（glm-4-flash，低成本）"),
        LlmProvider.OpenRouter => ProviderCapabilities.Cloud(0, 1.0, ProviderLatency.Medium, ProviderQualityLevel.High,
            "OpenRouter 聚合路由"),
        LlmProvider.Groq => ProviderCapabilities.Cloud(0, 0.3, ProviderLatency.Low, ProviderQualityLevel.Normal,
            "Groq（llama-3.3-70b，超低延迟）"),
        LlmProvider.SiliconFlow => ProviderCapabilities.Cloud(0, 0.7, ProviderLatency.Medium, ProviderQualityLevel.Normal,
            "硅基流动 SiliconFlow"),
        LlmProvider.DashScope => ProviderCapabilities.Cloud(0, 0.8, ProviderLatency.Medium, ProviderQualityLevel.Normal,
            "阿里百炼 DashScope（qwen-plus）"),
        LlmProvider.Ark => ProviderCapabilities.Cloud(0, 0.8, ProviderLatency.Medium, ProviderQualityLevel.Normal,
            "火山方舟 Ark"),
        LlmProvider.Azure => ProviderCapabilities.Cloud(0, 1.5, ProviderLatency.Medium, ProviderQualityLevel.High,
            "Azure OpenAI"),
        _ => ProviderCapabilities.Cloud(0, 1.0, ProviderLatency.Medium, ProviderQualityLevel.Normal, "Unknown LLM")
    };
}
