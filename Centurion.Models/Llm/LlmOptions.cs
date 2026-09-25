namespace Centurion.Models.Llm;

/// <summary>
/// LLM 连接配置：模型名、API 密钥、BaseUrl 与服务提供商。
/// 服务商解析规则见 <c>LlmEndpointParser</c>：显式 Provider 优先，其次按 BaseUrl 主机名推断，
/// 最后按"有 API 密钥 → OpenAI 官方，无 → 本地 Ollama"回退。
/// </summary>
public sealed class LlmOptions
{
    /// <summary>模型名称；为空时使用所选提供商的默认模型（见 <c>LlmProviderRegistry</c>）。</summary>
    public string? Model { get; set; }

    /// <summary>API 密钥；Ollama 本地服务无需提供。</summary>
    public string? ApiKey { get; set; }

    /// <summary>自定义 API 端点；为空时使用所选/推断提供商的默认端点。</summary>
    public string? BaseUrl { get; set; }

    /// <summary>服务提供商；默认 <c>LlmProvider.Auto</c> 自动推断。</summary>
    public LlmProvider Provider { get; set; } = LlmProvider.Auto;

    /// <summary>以字符串形式指定的提供商名（如 "deepseek"、"openrouter"、"azure"）；解析见 <c>LlmEndpointParser</c>。</summary>
    public string? ProviderName { get; set; }
}
