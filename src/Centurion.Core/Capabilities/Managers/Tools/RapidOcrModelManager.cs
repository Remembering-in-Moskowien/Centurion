using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Capabilities.Managers.Tools;

/// <summary>
/// RapidOCR (PaddleOCR ONNX) model management: on demand, downloads the PP-OCRv6 small
/// multilingual model set from ModelScope (single model recognizing 87 languages including
/// Chinese/English), caches it locally under content SHA-256 names, and verifies the published
/// SHA-256 during download.
/// Model files:
///   - PP-OCRv6_det_small.onnx   (text detection, DBNet)
///   - PP-OCRv6_rec_small.onnx   (recognition, CRNN, multilingual)
///   - ppocrv6_small_dict.txt    (recognition dictionary, must match the rec model)
/// Files are stored as <c>&lt;sha256&gt;.onnx</c> / <c>&lt;sha256&gt;.txt</c> under
/// tools/rapidocr/models/ with a per-directory manifest mapping logical roles (det/rec/dict) to
/// hashes. The 180-degree orientation classifier reuses the PP-OCRv5 cls model under models/v5,
/// so no download is needed here.
/// </summary>
public sealed class RapidOcrModelManager(ILogger<RapidOcrModelManager> logger)
{
    private static readonly HttpClient HttpClient = CreateHttpClient();

    private static readonly string ModelsRoot =
        Path.Combine(AppContext.BaseDirectory, "tools", "rapidocr", "models");

    private const string ModelScopeBase =
        "https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.9.2";

    private const string ManifestFileName = ".manifest.json";

    /// <summary>Relative name of the detection model.</summary>
    public const string DetModelFile = "PP-OCRv6_det_small.onnx";

    /// <summary>Relative name of the recognition model.</summary>
    public const string RecModelFile = "PP-OCRv6_rec_small.onnx";

    /// <summary>Relative name of the recognition dictionary.</summary>
    public const string DictFile = "ppocrv6_small_dict.txt";

    private static readonly (string Role, string FileName, string Url, string Sha256)[] ModelManifest =
    [
        ("det", DetModelFile, $"{ModelScopeBase}/onnx/PP-OCRv6/det/PP-OCRv6_det_small.onnx",
            "090f04abcd9d9a7498bc4ebf677e4cb9bdce1fe4197ddb7e529f1ef44e1ff94f"),
        ("rec", RecModelFile, $"{ModelScopeBase}/onnx/PP-OCRv6/rec/PP-OCRv6_rec_small.onnx",
            "6f327246b50388f3c176ae304bd95767ea6dc0c9ae92153ef8cbe210b3c14884"),
        ("dict", DictFile, $"{ModelScopeBase}/paddle/PP-OCRv6/rec/PP-OCRv6_rec_small/ppocrv6_dict.txt", string.Empty)
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
        var manifest = LoadManifest();
        if (manifest is null)
            return null;

        string? Resolve(string role, string ext)
            => manifest.TryGetValue(role, out var entry) && File.Exists(Path.Combine(ModelsRoot, entry.Hash + ext))
                ? Path.Combine(ModelsRoot, entry.Hash + ext)
                : null;

        var det = Resolve("det", ".onnx");
        var rec = Resolve("rec", ".onnx");
        var dict = Resolve("dict", ".txt");
        return det is not null && rec is not null && dict is not null ? (det, rec, dict) : null;
    }

    /// <summary>
    /// Ensures the models are downloaded and verified. Returns the paths directly when ready;
    /// downloads them one by one when missing, storing each under its content SHA-256. When any
    /// model fails to download/verify, logs and returns null (the engine will fall back to the
    /// bundled latin models).
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
            foreach (var (role, fileName, url, sha256) in ModelManifest)
            {
                var ext = Path.GetExtension(fileName);
                var tmp = Path.Combine(ModelsRoot, $".download-{Guid.NewGuid():N}{ext}");
                try
                {
                    if (!await VerifyDownloadAsync(url, tmp, sha256, cancellationToken))
                        throw new InvalidDataException($"RapidOCR model '{fileName}' failed SHA-256 verification.");

                    var hash = Centurion.Core.Infrastructure.ContentHasher.ComputeFileSha256(tmp);
                    var final = Path.Combine(ModelsRoot, hash + ext);
                    if (!File.Exists(final))
                        File.Move(tmp, final);
                    await WriteManifestEntryAsync(role, hash);
                    logger.LogInformation("RapidOCR model {Role} installed ({Hash}).", role, hash);
                }
                finally
                {
                    TryDelete(tmp);
                }
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

    private static async Task<bool> VerifyDownloadAsync(string url, string destination, string expectedSha256, CancellationToken cancellationToken)
    {
        using var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var output = File.Create(destination))
            await input.CopyToAsync(output, cancellationToken);

        if (string.IsNullOrWhiteSpace(expectedSha256))
            return new FileInfo(destination).Length > 0;
        return (await ComputeFileSha256Async(destination, cancellationToken))
            .Equals(expectedSha256, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> ComputeFileSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    // ---------- Manifest ----------

    private async Task<Dictionary<string, ManifestEntry>?> LoadManifestAsync()
    {
        var path = Path.Combine(ModelsRoot, ManifestFileName);
        if (!File.Exists(path))
            return null;
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<Dictionary<string, ManifestEntry>>(stream)
                ?? new Dictionary<string, ManifestEntry>(StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private Dictionary<string, ManifestEntry>? LoadManifest()
    {
        var path = Path.Combine(ModelsRoot, ManifestFileName);
        if (!File.Exists(path))
            return null;
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, ManifestEntry>>(File.ReadAllText(path))
                ?? new Dictionary<string, ManifestEntry>(StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task WriteManifestEntryAsync(string role, string hash)
    {
        var manifest = await LoadManifestAsync() ?? new Dictionary<string, ManifestEntry>(StringComparer.OrdinalIgnoreCase);
        manifest[role] = new ManifestEntry { Hash = hash };
        await File.WriteAllTextAsync(Path.Combine(ModelsRoot, ManifestFileName),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed class ManifestEntry
    {
        public string Hash { get; set; } = "";
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Centurion/1.0");
        return client;
    }
}
