using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OllamaSharp;
using OpenAI;
using Centurion.Abstractions.Utils;

namespace Centurion.Core.Factories;

/// <summary>
/// LLM 聊天客户端工厂：统一创建 OpenAI（API 密钥）或本地 Ollama 客户端，
/// 供分句、翻译等 LLM 策略复用。
/// </summary>
public static class LlmClientFactory
{
    /// <summary>
    /// 按配置创建聊天客户端：提供 API 密钥时使用 OpenAI 后端，否则使用本地 Ollama。
    /// </summary>
    /// <param name="model">模型名称；为空时按后端默认（OpenAI gpt-4o-mini / Ollama llama3.1）。</param>
    /// <param name="apiKey">OpenAI API 密钥；为空时回退 Ollama。</param>
    /// <param name="logger">创建 Ollama 客户端失败时记录错误的日志器。</param>
    /// <returns>配置好的聊天客户端。</returns>
    public static IChatClient Create(string? model, string? apiKey, ILogger logger)
    {
        if (!string.IsNullOrEmpty(apiKey))
        {
            var finalModel = model ?? "gpt-4o-mini";
            var client = new OpenAIClient(apiKey);
            return client.GetChatClient(finalModel).AsIChatClient();
        }

        return CreateOllama(model ?? "llama3.1", logger);
    }

    /// <summary>创建本地 Ollama 聊天客户端（默认地址 http://localhost:11434）。</summary>
    /// <param name="model">Ollama 模型名。</param>
    /// <param name="logger">记录创建失败的日志器。</param>
    /// <returns>配置好的 Ollama 客户端。</returns>
    private static IChatClient CreateOllama(string model, ILogger logger)
    {
        try
        {
            var httpClient = new HttpClient
            {
                BaseAddress = new Uri("http://localhost:11434"),
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
