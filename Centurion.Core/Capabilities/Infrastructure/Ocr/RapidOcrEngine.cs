using Centurion.Core.Capabilities.Managers.Tools;
using Centurion.Models.Providers;
using Microsoft.Extensions.Logging;
using RapidOcrNet;

namespace Centurion.Core.Capabilities.Infrastructure.Ocr;

/// <summary>
/// 本地 RapidOCR 引擎（RapidOcrNet，PaddleOCR ONNX，纯 CPU）。
/// 优先加载 PP-OCRv6 small 多语言模型（中文/英文等）；模型未下载时回退
/// RapidOcrNet 包内置的 PP-OCRv5 latin 模型（英文/数字）。线程安全、懒初始化。
/// </summary>
public sealed class RapidOcrEngine(
    RapidOcrModelManager modelManager,
    ILogger<RapidOcrEngine> logger)
{
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private RapidOcr? _ocr;
    private RapidOcrOptions _options = RapidOcrOptions.Default;

    /// <summary>引擎是否可加载（首次初始化后为 true；模型与运行库都缺失时为 false）。</summary>
    public bool IsInitialized => _ocr is not null;

    /// <summary>探测可用性：尝试初始化（不下载模型）。</summary>
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
    {
        var ocr = await GetOcrAsync(initOnMissing: false, cancellationToken);
        return ocr is not null;
    }

    /// <summary>
    /// 对单张图片做字幕级 OCR，返回识别文本（每行一条；无文本返回空字符串）。
    /// 引擎不可用时抛出 <see cref="ProviderUnavailableException"/>。
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

    private async Task<RapidOcr?> GetOcrAsync(bool initOnMissing, CancellationToken cancellationToken)
    {
        if (_ocr is not null)
            return _ocr;

        await _initGate.WaitAsync(cancellationToken);
        try
        {
            if (_ocr is not null)
                return _ocr;

            try
            {
                // 首选：PP-OCRv6 small 多语言（中英字幕通用）——模型缺失时尝试下载
                var models = initOnMissing
                    ? await modelManager.EnsureModelsAsync(cancellationToken)
                    : modelManager.ModelPaths();
                var v5ClsPath = Path.Combine(AppContext.BaseDirectory, "models", "v5",
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

                // 回退：包内置 PP-OCRv5 latin（英文/数字）——路径必须绝对化（RapidOcrNet 按相对 cwd 解析）
                var latin = new RapidOcr();
                var latinSet = RapidOcrModelSet.PPOCRv5Latin with
                {
                    DetModelPath = Path.Combine(AppContext.BaseDirectory, "models", "v5",
                        "ch_PP-OCRv5_mobile_det.onnx"),
                    ClsModelPath = v5ClsPath,
                    RecModelPath = Path.Combine(AppContext.BaseDirectory, "models", "v5",
                        "latin_PP-OCRv5_rec_mobile_infer.onnx"),
                    KeysPath = Path.Combine(AppContext.BaseDirectory, "models", "v5",
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
}
