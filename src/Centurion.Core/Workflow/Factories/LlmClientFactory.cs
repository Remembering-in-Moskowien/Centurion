using Centurion.Core.Capabilities.Infrastructure.Llm;using Centurion.Models.Llm;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OllamaSharp;
using OpenAI;
using System.ClientModel;

namespace Centurion.Core.Workflow.Factories;

/// <summary>
/// LLM chat client factory: uniformly creates clients for common OpenAI-compatible services (DeepSeek,
/// Moonshot, Zhipu, OpenRouter, Groq, SiliconFlow, DashScope, Ark, Azure, etc.) or a local Ollama client,
/// reused by LLM-based strategies such as sentence splitting and translation. Provider detection and
/// endpoint/default-model completion are handled by <see cref="LlmEndpointParser"/>.
/// </summary>
public static class LlmClientFactory
{
    /// <summary>
    /// Creates a chat client from the concise configuration (back-compat with the old signature): uses the
    /// official OpenAI service when an API key is provided, otherwise the local Ollama.
    /// </summary>
    /// <param name="model">The model name; when empty the backend default applies (OpenAI gpt-4o-mini / Ollama llama3.1).</param>
    /// <param name="apiKey">The OpenAI API key; when empty it falls back to Ollama.</param>
    /// <param name="logger">Logger used when client creation fails or a configuration warning is raised.</param>
    /// <returns>A configured chat client.</returns>
    public static IChatClient Create(string? model, string? apiKey, ILogger logger) =>
        Create(new LlmOptions { Model = model, ApiKey = apiKey }, logger);

    /// <summary>
    /// Creates a chat client from the full configuration: uses the explicit/inferred provider along with its
    /// endpoint and default model. Ollama goes through the local API; all other providers are OpenAI-compatible
    /// endpoints connected uniformly via the official OpenAI SDK <see cref="OpenAIClient"/> (with a custom
    /// <see cref="OpenAIClientOptions.Endpoint"/>).
    /// </summary>
    /// <param name="options">LLM connection options (model/key/endpoint/provider).</param>
    /// <param name="logger">Logger used when client creation fails or a configuration warning is raised.</param>
    /// <returns>A configured chat client.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the endpoint or model is missing or creation fails.</exception>
    public static IChatClient Create(LlmOptions options, ILogger logger)
    {
        var (provider, baseUrl, model) = LlmEndpointParser.Resolve(options, logger);

        if (provider == LlmProvider.Ollama)
            return CreateOllama(model ?? "llama3.2", logger, baseUrl);

        if (string.IsNullOrEmpty(baseUrl))
            throw new InvalidOperationException(
                $"LLM provider {LlmProviderRegistry.GetDisplayName(provider)} requires a base URL (--llm-base-url).");

        if (string.IsNullOrEmpty(options.ApiKey))
            logger.LogWarning(
                "LLM provider {Provider} usually requires an API key; proceeding without one.",
                LlmProviderRegistry.GetDisplayName(provider));

        try
        {
            var clientOptions = new OpenAIClientOptions { Endpoint = new Uri(baseUrl) };
            var client = new OpenAIClient(new ApiKeyCredential(options.ApiKey ?? string.Empty), clientOptions);
            return client.GetChatClient(model ?? string.Empty).AsIChatClient();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create LLM client for provider {Provider} at {BaseUrl}",
                LlmProviderRegistry.GetDisplayName(provider), baseUrl);
            throw new InvalidOperationException(
                $"Failed to initialize {LlmProviderRegistry.GetDisplayName(provider)} client: {ex.Message}", ex);
        }
    }

    /// <summary>Creates a local Ollama chat client.</summary>
    /// <param name="model">The Ollama model name.</param>
    /// <param name="logger">Logger used to record creation failures.</param>
    /// <param name="baseUrl">The Ollama service address; defaults to http://localhost:11434 when empty.</param>
    /// <returns>A configured Ollama client.</returns>
    private static IChatClient CreateOllama(string model, ILogger logger, string? baseUrl)
    {
        try
        {
            var httpClient = new HttpClient
            {
                BaseAddress = new Uri(baseUrl ?? "http://localhost:11434"),
                Timeout = TimeSpan.FromMinutes(5)
            };

            var client = new OllamaApiClient(httpClient);
            client.SelectedModel = model;
            return client;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to create Ollama client for model {Model}", model);
            throw new InvalidOperationException($"Failed to initialize Ollama client: {ex.Message}", ex);
        }
    }
}
