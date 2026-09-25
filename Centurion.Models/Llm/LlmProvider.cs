namespace Centurion.Models.Llm;

/// <summary>
/// LLM 服务提供商枚举。Auto 表示由 <c>LlmEndpointParser</c> 依据 BaseUrl/ApiKey 自动推断，
/// 其余成员对应常见的 OpenAI 兼容 API 服务商（各自默认端点与默认模型见 <c>LlmProviderRegistry</c>）。
/// </summary>
public enum LlmProvider
{
    /// <summary>自动推断：有 BaseUrl 按主机名识别，否则有 API 密钥走 OpenAI 官方、无密钥走本地 Ollama。</summary>
    Auto,

    /// <summary>OpenAI 官方（https://api.openai.com/v1）。</summary>
    OpenAI,

    /// <summary>DeepSeek（https://api.deepseek.com），OpenAI 兼容。</summary>
    DeepSeek,

    /// <summary>Moonshot / Kimi（https://api.moonshot.cn/v1），OpenAI 兼容。</summary>
    Moonshot,

    /// <summary>智谱 GLM（https://open.bigmodel.cn/api/paas/v4），OpenAI 兼容。</summary>
    Zhipu,

    /// <summary>OpenRouter 聚合（https://openrouter.ai/api/v1），模型名必填。</summary>
    OpenRouter,

    /// <summary>Groq（https://api.groq.com/openai/v1），OpenAI 兼容。</summary>
    Groq,

    /// <summary>硅基流动 SiliconFlow（https://api.siliconflow.cn/v1），OpenAI 兼容。</summary>
    SiliconFlow,

    /// <summary>阿里云百炼 DashScope 兼容模式（https://dashscope.aliyuncs.com/compatible-mode/v1）。</summary>
    DashScope,

    /// <summary>火山方舟 Ark（https://ark.cn-beijing.volces.com/api/v3），OpenAI 兼容；模型名需为推理接入点 ID。</summary>
    Ark,

    /// <summary>Azure OpenAI 兼容端点（https://{资源}.openai.azure.com/openai/v1），模型名需为部署名。</summary>
    Azure,

    /// <summary>本地 Ollama（http://localhost:11434），无需 API 密钥。</summary>
    Ollama
}
