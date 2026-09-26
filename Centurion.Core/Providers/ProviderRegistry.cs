using Centurion.Abstractions.Providers;
using Centurion.Abstractions.Tts;
using Centurion.Core.Capabilities.Infrastructure.Asr;
using Centurion.Core.Capabilities.Infrastructure.Llm;
using Centurion.Core.Capabilities.Infrastructure.Ocr;
using Centurion.Core.Providers.Asr;
using Centurion.Core.Providers.Diarization;
using Centurion.Core.Providers.Llm;
using Centurion.Core.Providers.Ocr;
using Centurion.Models.Asr;
using Centurion.Models.Llm;
using Centurion.Core.Providers.Tts;
using Centurion.Core.Providers.VocalSeparation;
using Centurion.Core.Workflow.Strategy.Diarization;
using Centurion.Core.Workflow.Strategy.Transcribe;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Centurion.Models.Providers;

namespace Centurion.Core.Providers;

/// <summary>
/// Provider 注册表：汇总全部可用的 Provider（ASR 本地 3 + 云 4、OCR 3、LLM 11、TTS 1、
/// 说话人分割 2、人声分离 1），支持按名称/域/形态查询。
/// 云端 Provider 的密钥/端点由 <see cref="ProviderFactory"/> 在解析时注入。
/// </summary>
public sealed class ProviderRegistry(IServiceProvider serviceProvider) : IProviderRegistry
{
    private readonly Lazy<IReadOnlyList<IProvider>> _all = new(() => Build(serviceProvider));

    /// <inheritdoc />
    public IReadOnlyList<IProvider> All => _all.Value;

    /// <inheritdoc />
    public IProvider? Find(string name) =>
        _all.Value.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <inheritdoc />
    public IReadOnlyList<IProvider> ForDomain(string domain)
    {
        var interfaceName = domain.StartsWith('I') ? domain : $"I{domain}";
        return _all.Value.Where(p => p.GetType().GetInterfaces().Any(i => i.Name == interfaceName)).ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<IProvider> OfKind(ProviderKind kind) =>
        _all.Value.Where(p => p.Capabilities.Kind == kind).ToList();

    private static IReadOnlyList<IProvider> Build(IServiceProvider sp)
    {
        var providers = new List<IProvider>();

        // ---- ASR：本地 3 + 云 4 ----
        providers.Add(new LocalAsrProvider(
            WhisperCppAsrProvider.Name, "Whisper.cpp",
            sp.GetRequiredService<WhisperCppStrategy>(), WhisperCppAsrProvider.Capabilities));
        providers.Add(new LocalAsrProvider(
            CrispAsrQwenProvider.Name, "CrispASR (Qwen3)",
            sp.GetRequiredService<CrispAsrQwenStrategy>(), CrispAsrQwenProvider.Capabilities));
        providers.Add(new LocalAsrProvider(
            CrispAsrWhisperProvider.Name, "CrispASR (Whisper)",
            sp.GetRequiredService<CrispAsrWhisperStrategy>(), CrispAsrWhisperProvider.Capabilities));

        foreach (var asrProvider in Enum.GetValues<AsrProvider>())
        {
            providers.Add(new CloudAsrProvider(
                CloudAsrProviders.NameFor(asrProvider),
                CloudAsrProviders.DisplayNameFor(asrProvider),
                asrProvider,
                sp.GetRequiredService<CloudAsrStrategy>(),
                CloudAsrProviders.CapabilitiesFor(asrProvider)));
        }

        // ---- OCR：智谱（云）+ Ollama / llama.cpp / RapidOCR（本地） ----
        var ocrClient = sp.GetRequiredService<OcrClient>();
        var rapidOcrEngine = sp.GetRequiredService<RapidOcrEngine>();
        foreach (var backend in new[] { OcrBackend.Zhipu, OcrBackend.Ollama, OcrBackend.LlamaCpp })
        {
            providers.Add(new OcrClientProvider(
                OcrProviders.NameFor(backend), $"{backend} OCR", backend, ocrClient,
                OcrProviders.CapabilitiesFor(backend)));
        }

        providers.Add(new OcrClientProvider(
            OcrProviders.NameFor(OcrBackend.RapidOcr), "RapidOCR OCR", OcrBackend.RapidOcr, ocrClient,
            OcrProviders.CapabilitiesFor(OcrBackend.RapidOcr), rapidOcrEngine));

        // ---- LLM：11 提供商（Ollama 本地 + 云端） ----
        foreach (var llmProvider in Enum.GetValues<LlmProvider>())
        {
            providers.Add(new OpenAiCompatibleLlmProvider(
                LlmProviders.NameFor(llmProvider), LlmProviderRegistry.GetDisplayName(llmProvider),
                llmProvider, LlmProviders.CapabilitiesFor(llmProvider),
                sp.GetRequiredService<ILogger<OpenAiCompatibleLlmProvider>>()));
        }

        // ---- TTS：llama-tts（本地） ----
        providers.Add(new LlamaTtsProvider(
            LlamaTtsProviders.Name, "llama.cpp llama-tts",
            sp.GetRequiredService<ITtsEngine>(), LlamaTtsProviders.Capabilities));

        // ---- 说话人分割：CrispASR / Pyannote（本地） ----
        providers.Add(new DiarizationStrategyProvider(
            DiarizationProviders.CrispAsrName, "CrispASR Diarization",
            sp.GetRequiredService<CrispAsrDiarizationStrategy>(), DiarizationProviders.CrispAsrCapabilities));
        providers.Add(new DiarizationStrategyProvider(
            DiarizationProviders.PyannoteName, "Pyannote + TitaNet",
            sp.GetRequiredService<PyannoteTitaNetDiarizationStrategy>(), DiarizationProviders.PyannoteCapabilities));

        // ---- 人声分离：demucs-rs（本地） ----
        providers.Add(new DemucsVocalSeparationProvider(
            DemucsVocalSeparationProviders.Name, "demucs-rs",
            sp.GetRequiredService<Centurion.Core.Workflow.Factories.IToolManagerFactory>(),
            sp.GetRequiredService<Centurion.Core.Capabilities.Managers.Runtime.ProcessManager>(),
            DemucsVocalSeparationProviders.Capabilities));

        return providers;
    }
}
