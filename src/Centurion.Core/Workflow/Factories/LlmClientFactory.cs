using Centurion.Core.Capabilities.Infrastructure.Llm;using Centurion.Models.Llm;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OllamaSharp;
using OpenAI;
using System.ClientModel;

namespace Centurion.Core.Workflow.Factories;

/// <summary>
/// LLM 聊天客户端工厂：统一创建各常见 OpenAI 兼容服务（DeepSeek、Moonshot、智谱、OpenRouter、Groq、
/// SiliconFlow、DashScope、方舟、Azure 等）或本地 Ollama 客户端，供分句、翻译等 LLM 策略复用。
/// 服务商识别与端点/默认模型补全由 <see cref="LlmEndpointParser"/> 完成。
/// </summary>
public static class LlmClientFactory
{
    /// <summary>
    /// 按简洁配置创建聊天客户端（兼容旧签名）：提供 API 密钥时使用 OpenAI 官方，否则使用本地 Ollama。
    /// </summary>
    /// <param name="model">模型名称；为空时按后端默认（OpenAI gpt-4o-mini / Ollama llama3.1）。</param>
    /// <param name="apiKey">OpenAI API 密钥；为空时回退 Ollama。</param>
    /// <param name="logger">创建客户端失败或配置告警时记录日志的日志器。</param>
    /// <returns>配置好的聊天客户端。</returns>
    public static IChatClient Create(string? model, string? apiKey, ILogger logger) =>
        Create(new LlmOptions { Model = model, ApiKey = apiKey }, logger);

    /// <summary>
    /// 按完整配置创建聊天客户端：显式/推断提供商，使用其端点与默认模型。
    /// Ollama 走本地 API；其余提供商均为 OpenAI 兼容端点，统一用官方 OpenAI SDK 的
    /// <see cref="OpenAIClient"/>（自定义 <see cref="OpenAIClientOptions.Endpoint"/>）连接。
    /// </summary>
    /// <param name="options">LLM 连接配置（模型/密钥/端点/提供商）。</param>
    /// <param name="logger">创建客户端失败或配置告警时记录日志的日志器。</param>
    /// <returns>配置好的聊天客户端。</returns>
    /// <exception cref="InvalidOperationException">端点或模型缺失、创建失败时抛出。</exception>
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

    /// <summary>创建本地 Ollama 聊天客户端。</summary>
    /// <param name="model">Ollama 模型名。</param>
    /// <param name="logger">记录创建失败的日志器。</param>
    /// <param name="baseUrl">Ollama 服务地址；为空时默认 http://localhost:11434。</param>
    /// <returns>配置好的 Ollama 客户端。</returns>
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
