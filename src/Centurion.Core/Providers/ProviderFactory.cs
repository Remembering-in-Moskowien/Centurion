using Centurion.Abstractions.Providers;
using Centurion.Abstractions.Strategy;
using Centurion.Core.Capabilities.Infrastructure.Asr;
using Centurion.Core.Capabilities.Infrastructure.Llm;
using Centurion.Core.Capabilities.Infrastructure.Ocr;
using Centurion.Core.Providers.Asr;
using Centurion.Core.Providers.Llm;
using Centurion.Core.Providers.Ocr;
using Centurion.Models.Asr;
using Centurion.Models.Llm;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Centurion.Models.Providers;

namespace Centurion.Core.Providers;

/// <summary>
/// Provider factory: resolves a single provider and the fallback chain from configuration.
/// Keys are resolved centrally via <see cref="ApiKeyStore"/> (explicit config → environment variable → null).
/// </summary>
public sealed class ProviderFactory(
    IProviderRegistry registry) : IProviderFactory
{
    private ProviderPolicies? _policies;

    /// <inheritdoc />
    public ProviderPolicyOptions Policies => (PoliciesImpl).Options;

    private ProviderPolicies PoliciesImpl => _policies ??= new ProviderPolicies(
        ProviderPolicyOptions.ForProfile(ProviderProfileResolver.Current));

    /// <inheritdoc />
    public IReadOnlyList<IAsrProvider> CreateAsrChain(
        string engine, string? model, AsrOptions? options, ProviderProfile profile)
    {
        var engineLower = engine?.ToLowerInvariant() ?? "crispasr";

        // Resolve the primary provider: a cloud provider or a local engine
        IAsrProvider? primary = null;
        var cloud = AsrEndpointParser.Resolve(engineLower);
        if (cloud is not null)
        {
            primary = GetCloudAsr(cloud.Value.Provider, options);
        }
        else
        {
            primary = engineLower switch
            {
                "whispercpp" or "whisper.cpp" or "whisper-cpp" or "whisper-cli" or "whisper"
                    => GetLocalAsr(WhisperCppAsrProvider.Name),
                "crispasr-whisper" or "crisp-whisper"
                    => GetLocalAsr(CrispAsrWhisperProvider.Name),
                "crispasr" or "crisp" or "crispasr-qwen" or "crisp-qwen"
                    => GetLocalAsr(CrispAsrQwenProvider.Name),
                _ => null // Unknown engine: left for the NotSupportedException below
            };
        }

        if (primary is null)
            throw new NotSupportedException($"ASR engine '{engine}' is not supported.");

        // Backup provider: chosen by profile and the primary's kind (local and cloud back each other up)
        IAsrProvider? backup = null;
        if (primary.Capabilities.Kind == ProviderKind.Cloud)
        {
            // Cloud-first: local fallback (CrispASR-Qwen by default)
            backup = GetLocalAsr(CrispAsrQwenProvider.Name);
        }
        else if (ProviderProfileResolver.AllowsCloud(profile))
        {
            // Local-first: pick the first available cloud provider (with a key) as the fallback
            backup = FindFirstAvailableCloudAsr();
        }

        // Profile corrections:
        // - Offline: force local (ignore the configured cloud primary)
        // - Cheap: local-first (local stays primary even when a cloud one is configured)
        if (profile == ProviderProfile.Offline && primary.Capabilities.Kind == ProviderKind.Cloud)
        {
            primary = GetLocalAsr(CrispAsrQwenProvider.Name);
            backup = null;
        }
        else if (profile == ProviderProfile.Cheap && primary.Capabilities.Kind == ProviderKind.Cloud)
        {
            var local = GetLocalAsr(CrispAsrQwenProvider.Name);
            backup = primary;
            primary = local;
        }

        return backup is null ? [primary!] : [primary!, backup];
    }

    /// <inheritdoc />
    public IOcrProvider CreateOcrProvider(string backend, string? model, string? apiKey, string? baseUrl)
    {
        var name = backend.ToLowerInvariant();
        var provider = registry.Find(name) as IOcrProvider
                       ?? throw new NotSupportedException($"OCR backend '{backend}' is not supported.");

        if (provider is OcrClientProvider ocr)
        {
            ocr.ApiKey = ApiKeyStore.Resolve("ocr", apiKey);
            ocr.BaseUrl = ApiKeyStore.ResolveBaseUrl("ocr", baseUrl);
            ocr.DefaultModel = model;
        }
        return provider;
    }

    /// <inheritdoc />
    public ILlmProvider CreateLlmProvider(string provider, string? model, string? apiKey, string? baseUrl)
    {
        var name = LlmProviders.NameFor(ParseLlmProvider(provider));
        var instance = registry.Find(name) as ILlmProvider
                       ?? throw new NotSupportedException($"LLM provider '{provider}' is not supported.");

        if (instance is OpenAiCompatibleLlmProvider llm)
        {
            llm.ApiKey = ApiKeyStore.Resolve("llm", apiKey);
            llm.BaseUrl = ApiKeyStore.ResolveBaseUrl("llm", baseUrl);
            llm.DefaultModel = model;
        }
        return instance;
    }

    private IAsrProvider? GetLocalAsr(string name) => registry.Find(name) as IAsrProvider;

    private IAsrProvider GetCloudAsr(AsrProvider provider, AsrOptions? options)
    {
        var instance = registry.Find(CloudAsrProviders.NameFor(provider)) as CloudAsrProvider
                       ?? throw new InvalidOperationException($"Cloud ASR provider '{provider}' is not registered.");
        instance.ApiKey = ApiKeyStore.Resolve("asr", options?.ApiKey);
        instance.BaseUrl = ApiKeyStore.ResolveBaseUrl("asr", options?.BaseUrl);
        return instance;
    }

    private IAsrProvider? FindFirstAvailableCloudAsr()
    {
        var apiKey = ApiKeyStore.Resolve("asr", null);
        if (string.IsNullOrWhiteSpace(apiKey))
            return null;

        foreach (var provider in Enum.GetValues<AsrProvider>())
        {
            var instance = registry.Find(CloudAsrProviders.NameFor(provider)) as CloudAsrProvider;
            if (instance is null)
                continue;
            instance.ApiKey = apiKey;
            return instance;
        }
        return null;
    }

    private static LlmProvider ParseLlmProvider(string provider)
    {
        if (Enum.TryParse<LlmProvider>(provider, ignoreCase: true, out var parsed))
            return parsed;
        return provider.ToLowerInvariant() switch
        {
            "openai" => LlmProvider.OpenAI,
            "deepseek" => LlmProvider.DeepSeek,
            "moonshot" or "kimi" => LlmProvider.Moonshot,
            "zhipu" or "glm" => LlmProvider.Zhipu,
            "openrouter" => LlmProvider.OpenRouter,
            "groq" => LlmProvider.Groq,
            "siliconflow" => LlmProvider.SiliconFlow,
            "dashscope" or "qwen" => LlmProvider.DashScope,
            "ark" or "volcengine" => LlmProvider.Ark,
            "azure" => LlmProvider.Azure,
            "ollama" => LlmProvider.Ollama,
            _ => throw new NotSupportedException($"LLM provider '{provider}' is not supported.")
        };
    }
}
