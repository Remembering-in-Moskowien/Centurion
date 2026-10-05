using System.Linq;
using System.Net.Http;
using System.Text.Json;
using Centurion.Models.Llm;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Capabilities.Infrastructure.Llm;

/// <summary>
/// LLM endpoint parser: normalizes the user-supplied <see cref="LlmOptions"/> into a
/// concrete (provider, endpoint, model) triple. Resolution precedence:
/// <list type="number">
/// <item>An explicit <see cref="LlmOptions.Provider"/> (or <see cref="LlmOptions.ProviderName"/> string) → use that provider;</item>
/// <item>Otherwise, if <see cref="LlmOptions.BaseUrl"/> is provided → auto-detect the provider from the host name (if it cannot be recognized, treat it as an OpenAI-compatible custom endpoint);</item>
/// <item>Otherwise fall back to "API key present → official OpenAI, absent → local Ollama" (preserving legacy behavior).</item>
/// </list>
/// When no model name is given it is filled in with the selected provider's default
/// model; when no endpoint is given it is filled in with the default endpoint.
/// </summary>
public static class LlmEndpointParser
{
    /// <summary>Provider string → enum alias table (case-insensitive, tolerant of common spellings).</summary>
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
    /// Parses a provider name (string) into the enum; returns <see cref="LlmProvider.Auto"/> when unrecognized.
    /// </summary>
    /// <param name="name">Provider name or alias.</param>
    /// <returns>The corresponding provider enum; Auto for empty/unknown.</returns>
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
    /// Infers the provider from the API endpoint host name; returns <see cref="LlmProvider.Auto"/> when unrecognized.
    /// </summary>
    /// <param name="baseUrl">API endpoint URL.</param>
    /// <returns>The inferred provider; Auto when unrecognized.</returns>
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
    /// Normalized resolution: returns the final (provider, endpoint, model). Missing
    /// model/endpoint are filled in with the provider defaults; when the provider is
    /// unrecognized and no endpoint is given, falls back to "API key present → official
    /// OpenAI, absent → Ollama".
    /// </summary>
    /// <param name="options">The LLM connection configuration supplied by the user.</param>
    /// <param name="logger">Logger to which resolution warnings are written (e.g. a missing model/endpoint being filled in).</param>
    /// <returns>(provider, endpoint URL, model name).</returns>
    public static (LlmProvider Provider, string BaseUrl, string? Model) Resolve(LlmOptions options, ILogger logger)
    {
        var provider = options.Provider != LlmProvider.Auto
            ? options.Provider
            : ParseProvider(options.ProviderName);

        if (provider == LlmProvider.Auto)
            provider = InferFromUrl(options.BaseUrl);

        // Still unrecognized: API key present → official OpenAI, absent → local Ollama (preserve legacy behavior)
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
        // The Ollama default model may not match the model actually pulled locally, causing
        // whole-batch 404 failures; when no model is explicitly given, auto-detect a local
        // conversational model already installed in Ollama.
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

    /// <summary>Probes the first conversational model installed in the local Ollama (/api/tags); returns null on failure or when unreachable.</summary>
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
                    continue; // Embedding-only models cannot do conversational translation
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
