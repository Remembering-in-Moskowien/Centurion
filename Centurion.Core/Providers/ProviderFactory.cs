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
/// Provider 工厂：按配置解析单 Provider 与 fallback 链。
/// 密钥统一经 <see cref="ApiKeyStore"/> 解析（显式配置 → 环境变量 → null）。
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

        // 解析主 Provider：云提供商或本地引擎
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
                _ => null // 未知引擎：交给下方 NotSupportedException
            };
        }

        if (primary is null)
            throw new NotSupportedException($"ASR engine '{engine}' is not supported.");

        // 备用 Provider：按 profile 与主形态决定（本地 ↔ 云互备）
        IAsrProvider? backup = null;
        if (primary.Capabilities.Kind == ProviderKind.Cloud)
        {
            // 云优先：本地兜底（默认 CrispASR-Qwen）
            backup = GetLocalAsr(CrispAsrQwenProvider.Name);
        }
        else if (ProviderProfileResolver.AllowsCloud(profile))
        {
            // 本地优先：找第一个可用云端（有密钥）作兜底
            backup = FindFirstAvailableCloudAsr();
        }

        // profile 修正：
        // - Offline：强制本地（忽略配置的云主 Provider）
        // - Cheap：本地优先（配置云时本地仍为主）
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
