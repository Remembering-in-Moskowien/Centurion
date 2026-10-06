namespace Centurion.Models.Llm;

/// <summary>
/// LLM provider enumeration. Auto lets <c>LlmEndpointParser</c> infer the provider from BaseUrl and ApiKey.
/// Other values represent common OpenAI-compatible API providers; see <c>LlmProviderRegistry</c> for default endpoints and models.
/// </summary>
public enum LlmProvider
{
    /// <summary>Infer automatically: detect by hostname when BaseUrl is set; otherwise use official OpenAI with an API key or local Ollama without one.</summary>
    Auto,

    /// <summary>Official OpenAI (https://api.openai.com/v1).</summary>
    OpenAI,

    /// <summary>DeepSeek (https://api.deepseek.com), OpenAI-compatible.</summary>
    DeepSeek,

    /// <summary>Moonshot / Kimi (https://api.moonshot.cn/v1), OpenAI-compatible.</summary>
    Moonshot,

    /// <summary>Zhipu GLM (https://open.bigmodel.cn/api/paas/v4), OpenAI-compatible.</summary>
    Zhipu,

    /// <summary>OpenRouter aggregator (https://openrouter.ai/api/v1); the model name is required.</summary>
    OpenRouter,

    /// <summary>Groq (https://api.groq.com/openai/v1), OpenAI-compatible.</summary>
    Groq,

    /// <summary>SiliconFlow (https://api.siliconflow.cn/v1), OpenAI-compatible.</summary>
    SiliconFlow,

    /// <summary>Alibaba Cloud DashScope compatible mode (https://dashscope.aliyuncs.com/compatible-mode/v1).</summary>
    DashScope,

    /// <summary>Volcengine Ark (https://ark.cn-beijing.volces.com/api/v3), OpenAI-compatible; model name must be an inference endpoint ID.</summary>
    Ark,

    /// <summary>Azure OpenAI-compatible endpoint (https://{resource}.openai.azure.com/openai/v1); model name must be a deployment name.</summary>
    Azure,

    /// <summary>Local Ollama (http://localhost:11434); no API key required.</summary>
    Ollama
}
