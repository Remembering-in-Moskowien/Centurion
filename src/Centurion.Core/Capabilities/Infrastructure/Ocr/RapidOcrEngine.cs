using Centurion.Core.Capabilities.Managers.Tools;
using Centurion.Models.Providers;
using Microsoft.Extensions.Logging;
using RapidOcrNet;

namespace Centurion.Core.Capabilities.Infrastructure.Ocr;

/// <summary>
/// Local RapidOCR engine (RapidOcrNet, PaddleOCR ONNX, CPU-only). Prefers loading the
/// PP-OCRv6 small multilingual model (Chinese/English, etc.); when the model is not
/// downloaded, falls back to the PP-OCRv5 latin model bundled with the RapidOcrNet
/// package (English/digits). Thread-safe, lazily initialized.
/// </summary>
public sealed class RapidOcrEngine(
    RapidOcrModelManager modelManager,
    ILogger<RapidOcrEngine> logger)
{
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private RapidOcr? _ocr;
    private RapidOcrOptions _options = RapidOcrOptions.Default;

    /// <summary>Whether the engine can be loaded (true after first initialization; false when both the model and runtime are missing).</summary>
    public bool IsInitialized => _ocr is not null;

    /// <summary>Probes availability: attempts initialization (without downloading the model).</summary>
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
    {
        var ocr = await GetOcrAsync(initOnMissing: false, cancellationToken);
        return ocr is not null;
    }

    /// <summary>
    /// Ensures the engine is usable: allows auto-downloading the PP-OCRv6 model (falls
    /// back to the bundled PP-OCRv5 latin when the download fails). Called by the ocr
    /// command during its validation phase, so that the "probe without download" check
    /// does not block automatic model acquisition on first use.
    /// </summary>
    public async Task<bool> EnsureAvailableAsync(CancellationToken cancellationToken)
    {
        var ocr = await GetOcrAsync(initOnMissing: true, cancellationToken);
        return ocr is not null;
    }

    /// <summary>
    /// Runs subtitle-level OCR on a single image and returns the recognized text (one
    /// entry per line; an empty string when there is no text). Throws
    /// <see cref="ProviderUnavailableException"/> when the engine is unavailable.
    /// </summary>
    public async Task<string> OcrImageAsync(string imagePath, CancellationToken cancellationToken)
    {
        var ocr = await GetOcrAsync(initOnMissing: true, cancellationToken);
        if (ocr is null)
            throw new ProviderUnavailableException(
                "RapidOCR engine is not available: ONNX runtime or bundled models are missing.", "rapidocr");

        var result = await ocr.DetectAsync(imagePath, _options, null, cancellationToken);
        return result.StrRes ?? string.Empty;
    }

    private async Task<RapidOcr?> GetOcrAsync(bool initOnMissing, CancellationToken cancellationToken)    {
        if (_ocr is not null)
            return _ocr;

        await _initGate.WaitAsync(cancellationToken);
        try
        {
            if (_ocr is not null)
                return _ocr;

            try
            {
                // Preferred: PP-OCRv6 small multilingual (covers Chinese/English subtitles) — download the model when it is missing
                var models = initOnMissing
                    ? await modelManager.EnsureModelsAsync(cancellationToken)
                    : modelManager.ModelPaths();
                var v5ClsPath = ResolveV5Path("cls") ?? Path.Combine(AppContext.BaseDirectory, "models", "v5",
                    "ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx");
                if (models is not null)
                {
                    var ocr = new RapidOcr();
                    var v6Set = RapidOcrModelSet.PPOCRv6Small with
                    {
                        DetModelPath = models.Value.DetPath,
                        ClsModelPath = v5ClsPath,
                        RecModelPath = models.Value.RecPath,
                        KeysPath = models.Value.DictPath
                    };
                    ocr.InitModels(v6Set);
                    _ocr = ocr;
                    _options = RapidOcrOptions.PPOCRv6;
                    logger.LogInformation("RapidOCR initialized with PP-OCRv6 small (multilingual).");
                    return _ocr;
                }

                if (!initOnMissing)
                    return null;

                // Fallback: the package's bundled PP-OCRv5 latin (English/digits) — paths must be absolute (RapidOcrNet resolves them relative to the cwd)
                var latin = new RapidOcr();
                var latinSet = RapidOcrModelSet.PPOCRv5Latin with
                {
                    DetModelPath = ResolveV5Path("det") ?? Path.Combine(AppContext.BaseDirectory, "models", "v5",
                        "ch_PP-OCRv5_mobile_det.onnx"),
                    ClsModelPath = v5ClsPath,
                    RecModelPath = ResolveV5Path("rec") ?? Path.Combine(AppContext.BaseDirectory, "models", "v5",
                        "latin_PP-OCRv5_rec_mobile_infer.onnx"),
                    KeysPath = ResolveV5Path("dict") ?? Path.Combine(AppContext.BaseDirectory, "models", "v5",
                        "ppocrv5_latin_dict.txt")
                };
                latin.InitModels(latinSet);
                _ocr = latin;
                _options = RapidOcrOptions.Default;
                logger.LogInformation("RapidOCR initialized with bundled PP-OCRv5 latin.");
                return _ocr;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "RapidOCR initialization failed; engine unavailable.");
                return null;
            }
        }
        finally
        {
            _initGate.Release();
        }
    }

    /// <summary>
    /// Resolves a models/v5 member (cls/det/rec/dict) through the category manifest written by the
    /// content-hash migration; falls back to the legacy fixed filename when no manifest entry exists.
    /// </summary>
    private static string? ResolveV5Path(string role)
    {
        var v5Dir = Path.Combine(AppContext.BaseDirectory, "models", "v5");
        var manifestPath = Path.Combine(v5Dir, ".manifest.json");
        try
        {
            if (File.Exists(manifestPath))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(manifestPath));
                if (doc.RootElement.TryGetProperty(role, out var entry)
                    && entry.TryGetProperty("Hash", out var hash)
                    && hash.GetString() is { Length: > 0 } h)
                {
                    var ext = role == "dict" ? ".txt" : ".onnx";
                    var path = Path.Combine(v5Dir, h + ext);
                    if (File.Exists(path))
                        return path;
                }
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Fall through to the legacy names below.
        }
        return null;
    }
}
