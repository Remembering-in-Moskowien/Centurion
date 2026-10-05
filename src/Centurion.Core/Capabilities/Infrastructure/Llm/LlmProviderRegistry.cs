using Centurion.Models.Llm;

namespace Centurion.Core.Capabilities.Infrastructure.Llm;

/// <summary>
/// LLM provider registry: maintains each provider's (<see cref="LlmProvider"/>) default
/// API endpoint, default model and display name. When the model name is empty,
/// <c>LlmEndpointParser</c> fills it in from here; when the endpoint is empty it is
/// filled in from here as well.
/// </summary>
public static class LlmProviderRegistry
{
    /// <summary>Provider → (default endpoint, default model).</summary>
    private static readonly Dictionary<LlmProvider, (string BaseUrl, string? DefaultModel)> Known =
        new()
        {
            [LlmProvider.OpenAI] = ("https://api.openai.com/v1", "gpt-4o-mini"),
            [LlmProvider.DeepSeek] = ("https://api.deepseek.com", "deepseek-chat"),
            [LlmProvider.Moonshot] = ("https://api.moonshot.cn/v1", "moonshot-v1-8k"),
            [LlmProvider.Zhipu] = ("https://open.bigmodel.cn/api/paas/v4", "glm-4-flash"),
            [LlmProvider.OpenRouter] = ("https://openrouter.ai/api/v1", null),
            [LlmProvider.Groq] = ("https://api.groq.com/openai/v1", "llama-3.3-70b-versatile"),
            [LlmProvider.SiliconFlow] = ("https://api.siliconflow.cn/v1", "Qwen/Qwen2.5-7B-Instruct"),
            [LlmProvider.DashScope] = ("https://dashscope.aliyuncs.com/compatible-mode/v1", "qwen-plus"),
            [LlmProvider.Ark] = ("https://ark.cn-beijing.volces.com/api/v3", null),
            [LlmProvider.Azure] = ("https://{resource}.openai.azure.com/openai/v1", null),
            [LlmProvider.Ollama] = ("http://localhost:11434", "llama3.2")
        };

    /// <summary>Gets the provider's default endpoint; returns null for unknown providers.</summary>
    /// <param name="provider">Target provider.</param>
    /// <returns>The default endpoint URL, or null when unknown.</returns>
    public static string? GetDefaultBaseUrl(LlmProvider provider) =>
        Known.TryGetValue(provider, out var entry) ? entry.BaseUrl : null;

    /// <summary>Gets the provider's default model name; returns null for unknown providers or when there is no default.</summary>
    /// <param name="provider">Target provider.</param>
    /// <returns>The default model name, or null when there is no default.</returns>
    public static string? GetDefaultModel(LlmProvider provider) =>
        Known.TryGetValue(provider, out var entry) ? entry.DefaultModel : null;

    /// <summary>Gets the provider's display name (used in logs and help text).</summary>
    /// <param name="provider">Target provider.</param>
    /// <returns>The display name.</returns>
    public static string GetDisplayName(LlmProvider provider) => provider switch
    {
        LlmProvider.OpenAI => "OpenAI",
        LlmProvider.DeepSeek => "DeepSeek",
        LlmProvider.Moonshot => "Moonshot (Kimi)",
        LlmProvider.Zhipu => "Zhipu GLM",
        LlmProvider.OpenRouter => "OpenRouter",
        LlmProvider.Groq => "Groq",
        LlmProvider.SiliconFlow => "SiliconFlow",
        LlmProvider.DashScope => "DashScope (Alibaba)",
        LlmProvider.Ark => "Volcano Ark",
        LlmProvider.Azure => "Azure OpenAI",
        LlmProvider.Ollama => "Ollama (local)",
        _ => "Auto"
    };
}
