using System.Linq;
using System.Net.Http;
using System.Text.Json;
using Centurion.Models.Llm;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Capabilities.Infrastructure.Llm;

/// <summary>
/// LLM 端点解析器：把用户提供的 <see cref="LlmOptions"/> 归一为具体的（提供商、端点、模型）三元组。
/// 解析优先级：
/// <list type="number">
/// <item>显式 <see cref="LlmOptions.Provider"/>（或 <see cref="LlmOptions.ProviderName"/> 字符串）→ 使用该提供商；</item>
/// <item>否则若提供了 <see cref="LlmOptions.BaseUrl"/> → 按主机名自动识别提供商（识别不了则视为 OpenAI 兼容自定义端点）；</item>
/// <item>否则按"有 API 密钥 → OpenAI 官方，无 → 本地 Ollama"回退（兼容旧行为）。</item>
/// </list>
/// 模型名未提供时补全为所选提供商的默认模型；端点未提供时补全为默认端点。
/// </summary>
public static class LlmEndpointParser
{
    /// <summary>提供商字符串 → 枚举的别名表（大小写不敏感，容忍常见拼写）。</summary>
    private static readonly Dictionary<string, LlmProvider> ProviderAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["openai"] = LlmProvider.OpenAI,
        ["deepseek"] = LlmProvider.DeepSeek,
        ["ds"] = LlmProvider.DeepSeek,
        ["moonshot"] = LlmProvider.Moonshot,
        ["kimi"] = LlmProvider.Moonshot,
        ["zhipu"] = LlmProvider.Zhipu,
        ["glm"] = LlmProvider.Zhipu,
        ["bigmodel"] = LlmProvider.Zhipu,
        ["openrouter"] = LlmProvider.OpenRouter,
        ["groq"] = LlmProvider.Groq,
        ["siliconflow"] = LlmProvider.SiliconFlow,
        ["silicon"] = LlmProvider.SiliconFlow,
        ["dashscope"] = LlmProvider.DashScope,
        ["aliyun"] = LlmProvider.DashScope,
        ["qwen"] = LlmProvider.DashScope,
        ["ark"] = LlmProvider.Ark,
        ["volcengine"] = LlmProvider.Ark,
        ["volcano"] = LlmProvider.Ark,
        ["azure"] = LlmProvider.Azure,
        ["ollama"] = LlmProvider.Ollama
    };

    /// <summary>
    /// 解析提供商名称（字符串）为枚举；无法识别时返回 <see cref="LlmProvider.Auto"/>。
    /// </summary>
    /// <param name="name">提供商名称或别名。</param>
    /// <returns>对应的提供商枚举；空/未知返回 Auto。</returns>
    public static LlmProvider ParseProvider(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return LlmProvider.Auto;
        if (ProviderAliases.TryGetValue(name.Trim(), out var provider))
            return provider;
        if (Enum.TryParse<LlmProvider>(name.Trim(), ignoreCase: true, out var parsed))
            return parsed;
        return LlmProvider.Auto;
    }

    /// <summary>
    /// 按 API 端点主机名推断提供商；识别不了时返回 <see cref="LlmProvider.Auto"/>。
    /// </summary>
    /// <param name="baseUrl">API 端点 URL。</param>
    /// <returns>推断的提供商；无法识别时 Auto。</returns>
    public static LlmProvider InferFromUrl(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            return LlmProvider.Auto;

        if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri))
            return LlmProvider.Auto;

        var host = uri.Host.ToLowerInvariant();
        if (host == "localhost" || host == "127.0.0.1" || host.EndsWith(".local", StringComparison.Ordinal))
            return LlmProvider.Ollama;
        if (host == "api.openai.com" || host.EndsWith(".openai.com", StringComparison.Ordinal))
            return LlmProvider.OpenAI;
        if (host.Contains("deepseek", StringComparison.Ordinal))
            return LlmProvider.DeepSeek;
        if (host.Contains("moonshot", StringComparison.Ordinal) || host.Contains("kimi", StringComparison.Ordinal))
            return LlmProvider.Moonshot;
        if (host.Contains("bigmodel", StringComparison.Ordinal) || host.Contains("zhipu", StringComparison.Ordinal))
            return LlmProvider.Zhipu;
        if (host.Contains("openrouter", StringComparison.Ordinal))
            return LlmProvider.OpenRouter;
        if (host.Contains("groq", StringComparison.Ordinal))
            return LlmProvider.Groq;
        if (host.Contains("siliconflow", StringComparison.Ordinal) || host.Contains("silicon", StringComparison.Ordinal))
            return LlmProvider.SiliconFlow;
        if (host.Contains("dashscope", StringComparison.Ordinal) || host.Contains("aliyuncs", StringComparison.Ordinal))
            return LlmProvider.DashScope;
        if (host.Contains("volces", StringComparison.Ordinal) || host.Contains("volcengine", StringComparison.Ordinal))
            return LlmProvider.Ark;
        if (host.EndsWith(".openai.azure.com", StringComparison.Ordinal) || host.Contains("azure", StringComparison.Ordinal))
            return LlmProvider.Azure;
        return LlmProvider.Auto;
    }

    /// <summary>
    /// 归一化解析：返回最终 (提供商, 端点, 模型)。模型/端点为空时按提供商默认值补全；
    /// 未识别提供商且无端点时按"有 API 密钥 → OpenAI 官方，无 → Ollama"回退。
    /// </summary>
    /// <param name="options">用户提供的 LLM 连接配置。</param>
    /// <param name="logger">解析告警（模型/端点缺失被补全等）写入的日志器。</param>
    /// <returns>(提供商, 端点 URL, 模型名)。</returns>
    public static (LlmProvider Provider, string BaseUrl, string? Model) Resolve(LlmOptions options, ILogger logger)
    {
        var provider = options.Provider != LlmProvider.Auto
            ? options.Provider
            : ParseProvider(options.ProviderName);

        if (provider == LlmProvider.Auto)
            provider = InferFromUrl(options.BaseUrl);

        // 仍无法识别：有 API 密钥 → OpenAI 官方，无 → 本地 Ollama（保持旧行为）
        if (provider == LlmProvider.Auto)
            provider = string.IsNullOrEmpty(options.ApiKey) ? LlmProvider.Ollama : LlmProvider.OpenAI;

        var baseUrl = !string.IsNullOrWhiteSpace(options.BaseUrl)
            ? options.BaseUrl!.Trim()
            : LlmProviderRegistry.GetDefaultBaseUrl(provider);

        if (string.IsNullOrEmpty(baseUrl))
        {
            logger.LogWarning(
                "LLM provider {Provider} has no default endpoint; a base URL is required (--llm-base-url).",
                LlmProviderRegistry.GetDisplayName(provider));
            baseUrl = options.BaseUrl?.Trim() ?? string.Empty;
        }

        var model = !string.IsNullOrWhiteSpace(options.Model)
            ? options.Model
            : LlmProviderRegistry.GetDefaultModel(provider);
        // Ollama 默认模型（llama3.1）可能与本机实际拉取的模型不一致，导致 404 整批失败；
        // 未显式指定模型时自动探测本机 Ollama 已安装的对话模型。
        if (string.IsNullOrWhiteSpace(options.Model) && provider == LlmProvider.Ollama)
        {
            var probed = TryProbeLocalOllamaModel(baseUrl);
            if (!string.IsNullOrWhiteSpace(probed))
            {
                model = probed;
                logger.LogInformation("Using locally available Ollama model: {Model}", probed);
            }
        }

        if (string.IsNullOrEmpty(model))
            logger.LogWarning(
                "LLM provider {Provider} has no default model; a model name is required (--model).",
                LlmProviderRegistry.GetDisplayName(provider));

        return (provider, baseUrl, model);
    }

    /// <summary>
    /// 探测本地 Ollama（/api/tags）已安装的第一个对话模型；失败或不可达返回 null。
    /// </summary>
    private static string? TryProbeLocalOllamaModel(string baseUrl)
    {
        try
        {
            var endpoint = baseUrl.TrimEnd('/') + "/api/tags";
            using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(1500) };
            var json = client.GetStringAsync(endpoint).GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("models", out var models)) return null;
            foreach (var m in models.EnumerateArray())
            {
                var name = m.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (m.TryGetProperty("capabilities", out var caps) &&
                    caps.ValueKind == JsonValueKind.Array &&
                    caps.EnumerateArray().Any(x => x.GetString() == "embedding"))
                {
                    continue; // 仅 embedding 模型不能做对话翻译
                }
                return name;
            }
            return null;
        }
        catch
        {
            return null;
        }
    }
}
