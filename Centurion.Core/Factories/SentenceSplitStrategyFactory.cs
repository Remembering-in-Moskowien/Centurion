using Centurion.Abstractions.Factories;
using Centurion.Abstractions.Strategy;
using Centurion.Core.Strategy.SentenceSplit;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OllamaSharp;
using OpenAI;

namespace Centurion.Core.Factories;

/// <summary>
/// Sentence split strategy factory: rule-based or LLM-based (OpenAI/Ollama)
/// </summary>
public class SentenceSplitStrategyFactory(
    IServiceProvider serviceProvider,
    ILogger<SentenceSplitStrategyFactory> logger)
    : ISentenceSplitStrategyFactory
{
    public ISentenceSplitStrategy Create(string strategy, SplitOptions options, string? model = null, string? apiKey = null)
    {
        return strategy.ToLowerInvariant() switch
        {
            "rule" => serviceProvider.GetRequiredService<RuleBasedSplitStrategy>(),
            "catalyst" or "nlp" => serviceProvider.GetRequiredService<RuleBasedSplitStrategy>(),
            "llm" => CreateLLMStrategy(options, model, apiKey),
            _ => throw new NotSupportedException($"Split strategy '{strategy}' is not supported.")
        };
    }

    private ISentenceSplitStrategy CreateLLMStrategy(SplitOptions options, string? model, string? apiKey)
    {
        IChatClient chatClient;

        // 1. If API key is provided, use OpenAI
        if (!string.IsNullOrEmpty(apiKey))
        {
            chatClient = CreateOpenAIClient(model ?? "gpt-4o-mini", apiKey);
        }
        // 2. Otherwise default to local Ollama
        else
        {
            var finalModel = model ?? "llama3.1";
            chatClient = CreateOllamaClient(finalModel);
        }

        var llmLogger = serviceProvider.GetService<ILogger<LLMSplitStrategy>>();
        return new LLMSplitStrategy(chatClient, llmLogger);
    }

    private IChatClient CreateOpenAIClient(string model, string apiKey)
    {
        var client = new OpenAIClient(apiKey);
        return client.GetChatClient(model).AsIChatClient();
    }

    private IChatClient CreateOllamaClient(string model)
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
            logger.LogError(ex, "Failed to create Ollama client for model {Model}", model);
            throw new InvalidOperationException($"Failed to initialize Ollama client: {ex.Message}", ex);
        }
    }
}
