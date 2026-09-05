using Centurion.Core.Abstractions.Factories;
using Centurion.Core.Abstractions.Strategy;
using Centurion.Core.Strategy.Alignment;
using Centurion.Core.Strategy.SentenceSplit;
using Centurion.Core.Strategy.Transcribe;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OllamaSharp;
using OpenAI;

namespace Centurion.Core.Factories;

/// <summary>
/// Transcription strategy factory supporting multiple backends:
/// - whispercpp (Whisper.cpp CLI)
/// - crispasr-qwen (CrispASR with Qwen3)
/// - crispasr-whisper (CrispASR with Whisper)
/// - crispasr (alias for qwen)
/// - whisper (legacy, maps to whispercpp)
/// </summary>
public class TranscriptionStrategyFactory(IServiceProvider serviceProvider) : ITranscriptionStrategyFactory
{
    public ITranscriptionStrategy Create(string engine, string? model, string language, string? initialPrompt)
    {
        var engineLower = engine.ToLowerInvariant();

        return engineLower switch
        {
            // Whisper.cpp via external CLI
            "whispercpp" or "whisper.cpp" or "whisper-cpp" or "whisper-cli" or "whisper"
                => serviceProvider.GetRequiredService<WhisperCppStrategy>(),

            // CrispASR with Qwen3 backend (default)
            "crispasr" or "crisp" or "crispasr-qwen" or "crisp-qwen"
                => serviceProvider.GetRequiredService<CrispAsrQwenStrategy>(),

            // CrispASR with Whisper backend
            "crispasr-whisper" or "crisp-whisper"
                => serviceProvider.GetRequiredService<CrispAsrWhisperStrategy>(),

            // future extensions
            // "api" => serviceProvider.GetRequiredService<ApiTranscriptionStrategy>(),

            _ => throw new NotSupportedException($"Transcription engine '{engine}' is not supported.")
        };
    }
}

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

        var logger = serviceProvider.GetService<ILogger<LLMSplitStrategy>>();
        return new LLMSplitStrategy(chatClient, logger);
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

public class AlignmentStrategyFactory(IServiceProvider serviceProvider) : IAlignmentStrategyFactory
{
    public IAlignmentStrategy Create(string modelName)
    {
        // Use ActivatorUtilities to resolve the strategy with runtime modelName
        return ActivatorUtilities.CreateInstance<CrispAsrAlignmentStrategy>(
            serviceProvider, modelName);
    }
}