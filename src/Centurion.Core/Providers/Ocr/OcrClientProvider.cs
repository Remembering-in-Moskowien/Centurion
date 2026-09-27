using Centurion.Abstractions.Providers;
using Centurion.Core.Capabilities.Infrastructure.Ocr;
using Centurion.Models.Providers;

namespace Centurion.Core.Providers.Ocr;

/// <summary>
/// OCR Provider：适配 <see cref="OcrClient"/>，按后端（智谱云端 / Ollama / llama.cpp）实例化注册。
/// 本地后端可用性经服务探测（GET /models）；云端后端检查 API 密钥。
/// </summary>
public sealed class OcrClientProvider(
    string name,
    string displayName,
    OcrBackend backend,
    OcrClient client,
    ProviderCapabilities capabilities,
    RapidOcrEngine? rapidOcrEngine = null) : IOcrProvider
{
    private readonly OcrClient _client = client ?? throw new ArgumentNullException(nameof(client));

    /// <inheritdoc />
    public string Name { get; } = name;

    /// <inheritdoc />
    public string DisplayName { get; } = displayName;

    /// <inheritdoc />
    public ProviderCapabilities Capabilities { get; } = capabilities;

    /// <summary>API 密钥；仅云端后端（智谱）需要。</summary>
    public string? ApiKey { get; set; }

    /// <summary>自定义端点；为空按后端默认。</summary>
    public string? BaseUrl { get; set; }

    /// <summary>默认模型；为空按后端默认。</summary>
    public string? DefaultModel { get; set; }

    /// <inheritdoc />
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
    {
        if (backend == OcrBackend.Zhipu)
            return !string.IsNullOrWhiteSpace(ApiKey);
        if (backend == OcrBackend.RapidOcr)
            return rapidOcrEngine is not null && await rapidOcrEngine.IsAvailableAsync(cancellationToken);
        return await _client.ProbeAsync(backend, BaseUrl, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ProviderResult<string>> OcrImageAsync(
        string imagePath, string? model, CancellationToken cancellationToken)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var text = backend == OcrBackend.RapidOcr
            ? await (rapidOcrEngine
                ?? throw new InvalidOperationException("RapidOCR engine is not registered."))
                .OcrImageAsync(imagePath, cancellationToken)
            : await _client.OcrImageAsync(
                imagePath, backend, model ?? DefaultModel, ApiKey, BaseUrl, cancellationToken);
        sw.Stop();

        var usage = ProviderUsage.ForTokens(DisplayName, model, 0, EstimateTokens(text),
            Capabilities.CostPer1MTokensUsd, 0, sw.ElapsedMilliseconds);
        return new ProviderResult<string>(text, usage);
    }

    /// <summary>粗略按字符数估算输出 token（4 字符 ≈ 1 token，中文按 1.5 字符/token 近似）。</summary>
    private static int EstimateTokens(string text)
    {
        var cjk = text.Count(c => c >= 0x4E00 && c <= 0x9FFF);
        var other = text.Length - cjk;
        return (int)Math.Ceiling(other / 4.0 + cjk / 1.5);
    }
}

/// <summary>OCR Provider 注册工厂（名称/能力固定）。</summary>
public static class OcrProviders
{
    /// <summary>按后端返回注册名。</summary>
    public static string NameFor(OcrBackend backend) => backend switch
    {
        OcrBackend.Zhipu => "zhipu",
        OcrBackend.Ollama => "ollama",
        OcrBackend.LlamaCpp => "llamacpp",
        OcrBackend.RapidOcr => "rapidocr",
        _ => backend.ToString().ToLowerInvariant()
    };

    /// <summary>按后端返回能力声明。</summary>
    public static ProviderCapabilities CapabilitiesFor(OcrBackend backend) => backend switch
    {
        OcrBackend.Zhipu => ProviderCapabilities.Cloud(0, 1.5, ProviderLatency.Medium, ProviderQualityLevel.High,
            "智谱 GLM-OCR（云端，需 API 密钥，字幕级 OCR 专用）"),
        OcrBackend.Ollama => ProviderCapabilities.Local(false, ProviderLatency.Medium, ProviderQualityLevel.Normal,
            "本地 Ollama 视觉模型（qwen2.5vl 等，免密钥）"),
        OcrBackend.LlamaCpp => ProviderCapabilities.Local(false, ProviderLatency.Medium, ProviderQualityLevel.Normal,
            "本地 llama-server（GGUF 视觉模型，服务需已启动）"),
        OcrBackend.RapidOcr => ProviderCapabilities.Local(false, ProviderLatency.Low, ProviderQualityLevel.Normal,
            "本地 RapidOCR（PaddleOCR ONNX，纯 CPU，多语言字幕识别）"),
        _ => ProviderCapabilities.Local(false, ProviderLatency.Medium, ProviderQualityLevel.Normal, "Unknown OCR")
    };
}
