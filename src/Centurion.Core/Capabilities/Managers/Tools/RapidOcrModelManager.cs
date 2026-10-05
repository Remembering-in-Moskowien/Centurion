using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Capabilities.Managers.Tools;

/// <summary>
/// RapidOCR (PaddleOCR ONNX) model management: on demand, downloads the PP-OCRv6 small
/// multilingual model set from ModelScope (single model recognizing 87 languages including
/// Chinese/English), caches it locally, and verifies SHA-256.
/// Model files:
///   - PP-OCRv6_det_small.onnx   (text detection, DBNet)
///   - PP-OCRv6_rec_small.onnx   (recognition, CRNN, multilingual)
///   - ppocrv6_small_dict.txt    (recognition dictionary, must match the rec model)
/// The 180-degree orientation classifier reuses the PP-OCRv5 cls model bundled in the
/// RapidOcrNet package, so no download is needed.
/// </summary>
public sealed class RapidOcrModelManager(ILogger<RapidOcrModelManager> logger)
{
    private static readonly HttpClient HttpClient = CreateHttpClient();

    private static readonly string ModelsRoot =
        Path.Combine(AppContext.BaseDirectory, "tools", "rapidocr", "models");

    private const string ModelScopeBase =
        "https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.9.2";

    /// <summary>Relative name of the detection model.</summary>
    public const string DetModelFile = "PP-OCRv6_det_small.onnx";

    /// <summary>Relative name of the recognition model.</summary>
    public const string RecModelFile = "PP-OCRv6_rec_small.onnx";

    /// <summary>Relative name of the recognition dictionary.</summary>
    public const string DictFile = "ppocrv6_small_dict.txt";

    private static readonly (string FileName, string Url, string Sha256)[] ModelManifest =
    [
        (DetModelFile, $"{ModelScopeBase}/onnx/PP-OCRv6/det/PP-OCRv6_det_small.onnx",
            "090f04abcd9d9a7498bc4ebf677e4cb9bdce1fe4197ddb7e529f1ef44e1ff94f"),
        (RecModelFile, $"{ModelScopeBase}/onnx/PP-OCRv6/rec/PP-OCRv6_rec_small.onnx",
            "6f327246b50388f3c176ae304bd95767ea6dc0c9ae92153ef8cbe210b3c14884"),
        (DictFile, $"{ModelScopeBase}/paddle/PP-OCRv6/rec/PP-OCRv6_rec_small/ppocrv6_dict.txt", string.Empty)
    ];

    /// <summary>Model root directory.</summary>
    public static string Root => ModelsRoot;

    /// <summary>Whether the models are ready (all three present).</summary>
    public bool IsReady()
    {
        var paths = ModelPaths();
        return paths is not null;
    }

    /// <summary>Returns the local paths (det, rec, dict); returns null when not ready.</summary>
    public (string DetPath, string RecPath, string DictPath)? ModelPaths()
    {
        var det = Path.Combine(ModelsRoot, DetModelFile);
        var rec = Path.Combine(ModelsRoot, RecModelFile);
        var dict = Path.Combine(ModelsRoot, DictFile);
        if (File.Exists(det) && File.Exists(rec) && File.Exists(dict))
            return (det, rec, dict);
        return null;
    }

    /// <summary>
    /// Ensures the models are downloaded and verified. Returns the paths directly when ready;
    /// downloads them one by one when missing. When any model fails to download/verify, logs and
    /// returns null (the engine will fall back to the bundled latin models).
    /// </summary>
    public async Task<(string DetPath, string RecPath, string DictPath)?> EnsureModelsAsync(
        CancellationToken cancellationToken)
    {
        var existing = ModelPaths();
        if (existing is not null)
            return existing;

        try
        {
            Directory.CreateDirectory(ModelsRoot);
            foreach (var (fileName, url, sha256) in ModelManifest)
            {
                var target = Path.Combine(ModelsRoot, fileName);
                if (File.Exists(target) && await VerifyAsync(target, sha256, cancellationToken))
                    continue;

                if (File.Exists(target))
                    File.Delete(target);

                logger.LogInformation("Downloading RapidOCR model {Model} from ModelScope.", fileName);
                await DownloadAsync(url, target, cancellationToken);
                if (!await VerifyAsync(target, sha256, cancellationToken))
                    throw new InvalidDataException($"RapidOCR model '{fileName}' failed SHA-256 verification.");
            }

            return ModelPaths()
                ?? throw new InvalidDataException("RapidOCR models not found after download.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "RapidOCR model download failed; engine will fall back to bundled latin models.");
            return null;
        }
    }

    private static async Task DownloadAsync(string url, string destination, CancellationToken cancellationToken)
    {
        using var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = File.Create(destination);
        await input.CopyToAsync(output, cancellationToken);
    }

    private static async Task<bool> VerifyAsync(string path, string expectedSha256, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(expectedSha256))
            return new FileInfo(path).Length > 0;

        try
        {
            await using var stream = File.OpenRead(path);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
            return actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Centurion/1.0");
        return client;
    }
}
