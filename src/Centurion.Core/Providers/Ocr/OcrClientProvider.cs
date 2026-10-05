using Centurion.Abstractions.Providers;
using Centurion.Core.Capabilities.Infrastructure.Ocr;
using Centurion.Models.Providers;

namespace Centurion.Core.Providers.Ocr;

/// <summary>
/// OCR provider: adapts <see cref="OcrClient"/>, instantiated and registered per backend (Zhipu cloud / Ollama / llama.cpp).
/// Local backend availability is probed via a service probe (GET /models); cloud backends check the API key.
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

    /// <summary>API key; only required by cloud backends (Zhipu).</summary>
    public string? ApiKey { get; set; }

    /// <summary>Custom endpoint; falls back to the backend default when empty.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Default model; falls back to the backend default when empty.</summary>
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

    /// <summary>Roughly estimates output tokens by character count (≈1 token per 4 chars; CJK approximated at 1.5 chars/token).</summary>
    private static int EstimateTokens(string text)
    {
        var cjk = text.Count(c => c >= 0x4E00 && c <= 0x9FFF);
        var other = text.Length - cjk;
        return (int)Math.Ceiling(other / 4.0 + cjk / 1.5);
    }
}

/// <summary>OCR provider registration factory (fixed names/capabilities).</summary>
public static class OcrProviders
{
    /// <summary>Returns the registered name per backend.</summary>
    public static string NameFor(OcrBackend backend) => backend switch
    {
        OcrBackend.Zhipu => "zhipu",
        OcrBackend.Ollama => "ollama",
        OcrBackend.LlamaCpp => "llamacpp",
        OcrBackend.RapidOcr => "rapidocr",
        _ => backend.ToString().ToLowerInvariant()
    };

    /// <summary>Returns the capability declaration per backend.</summary>
    public static ProviderCapabilities CapabilitiesFor(OcrBackend backend) => backend switch
    {
        OcrBackend.Zhipu => ProviderCapabilities.Cloud(0, 1.5, ProviderLatency.Medium, ProviderQualityLevel.High,
            "Zhipu GLM-OCR (cloud, API key required, subtitle-grade OCR)"),
        OcrBackend.Ollama => ProviderCapabilities.Local(false, ProviderLatency.Medium, ProviderQualityLevel.Normal,
            "Local Ollama vision model (qwen2.5vl etc., keyless)"),
        OcrBackend.LlamaCpp => ProviderCapabilities.Local(false, ProviderLatency.Medium, ProviderQualityLevel.Normal,
            "Local llama-server (GGUF vision model, server must be running)"),
        OcrBackend.RapidOcr => ProviderCapabilities.Local(false, ProviderLatency.Low, ProviderQualityLevel.Normal,
            "Local RapidOCR (PaddleOCR ONNX, CPU-only, multilingual subtitle OCR)"),
        _ => ProviderCapabilities.Local(false, ProviderLatency.Medium, ProviderQualityLevel.Normal, "Unknown OCR")
    };
}
