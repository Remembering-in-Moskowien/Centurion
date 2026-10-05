namespace Centurion.Models.Llm;

/// <summary>
/// LLM connection settings: model, API key, BaseUrl, and provider.
/// Provider resolution is described by <c>LlmEndpointParser</c>: an explicit provider takes precedence, followed by BaseUrl hostname detection,
/// then a fallback to official OpenAI when an API key is present or local Ollama otherwise.
/// </summary>
public sealed class LlmOptions
{
    /// <summary>Model name; null uses the selected provider's default model (see <c>LlmProviderRegistry</c>).</summary>
    public string? Model { get; set; }

    /// <summary>API key; not required for local Ollama.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Custom API endpoint; null uses the selected or inferred provider's default endpoint.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Provider; defaults to <c>LlmProvider.Auto</c> for automatic detection.</summary>
    public LlmProvider Provider { get; set; } = LlmProvider.Auto;

    /// <summary>Provider name as a string, such as "deepseek", "openrouter", or "azure"; see <c>LlmEndpointParser</c> for resolution.</summary>
    public string? ProviderName { get; set; }
}
