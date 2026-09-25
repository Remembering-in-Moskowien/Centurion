using Centurion.Models.Llm;

namespace Centurion.Core.Llm;

/// <summary>
/// LLM 提供商注册表：维护各提供商（<see cref="LlmProvider"/>）的默认 API 端点、默认模型与显示名。
/// 模型名为空时由 <c>LlmEndpointParser</c> 据此补全；端点为空时同样据此补全。
/// </summary>
public static class LlmProviderRegistry
{
    /// <summary>提供商 → (默认端点, 默认模型)。</summary>
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
            [LlmProvider.Ollama] = ("http://localhost:11434", "llama3.1")
        };

    /// <summary>获取提供商的默认端点；未知提供商返回 null。</summary>
    /// <param name="provider">目标提供商。</param>
    /// <returns>默认端点 URL，未知时为 null。</returns>
    public static string? GetDefaultBaseUrl(LlmProvider provider) =>
        Known.TryGetValue(provider, out var entry) ? entry.BaseUrl : null;

    /// <summary>获取提供商的默认模型名；未知提供商或无默认模型时返回 null。</summary>
    /// <param name="provider">目标提供商。</param>
    /// <returns>默认模型名，无默认时为 null。</returns>
    public static string? GetDefaultModel(LlmProvider provider) =>
        Known.TryGetValue(provider, out var entry) ? entry.DefaultModel : null;

    /// <summary>获取提供商的显示名（用于日志与帮助文本）。</summary>
    /// <param name="provider">目标提供商。</param>
    /// <returns>显示名。</returns>
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
