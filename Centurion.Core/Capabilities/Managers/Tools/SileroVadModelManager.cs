using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Capabilities.Managers.Tools;

/// <summary>
/// Silero VAD（语音活动检测，ONNX）模型管理：按需从官方仓库下载
/// <c>silero_vad.onnx</c>（16 kHz 流式接口：input[1,1,512] + state[2,1,128] + sr），
/// 本地缓存并做 SHA-256 校验。模型缺失/下载失败时由调用方降级为能量型 VAD。
/// </summary>
public sealed class SileroVadModelManager(ILogger<SileroVadModelManager> logger)
{
    private static readonly HttpClient HttpClient = CreateHttpClient();

    /// <summary>模型本地路径（可执行目录下 tools\vad\models）。</summary>
    private static readonly string ModelPath =
        Path.Combine(AppContext.BaseDirectory, "tools", "vad", "models", "silero_vad.onnx");

    /// <summary>官方模型下载地址（snakers4/silero-vad 仓库）。</summary>
    public const string ModelUrl =
        "https://github.com/snakers4/silero-vad/raw/master/src/silero_vad/data/silero_vad.onnx";

    /// <summary>模型 SHA-256（2,327,524 字节，验证于 2026-09-26）。</summary>
    private const string ModelSha256 = "1a153a22f4509e292a94e67d6f9b85e8deb25b4988682b7e174c65279d8788e3";

    /// <summary>模型文件相对名（诊断/日志用）。</summary>
    public const string ModelFileName = "silero_vad.onnx";

    /// <summary>模型根目录。</summary>
    public static string Root => Path.GetDirectoryName(ModelPath)!;

    /// <summary>模型是否已就绪（本地存在且非空）。</summary>
    public bool IsReady()
    {
        var info = new FileInfo(ModelPath);
        return info.Exists && info.Length > 0;
    }

    /// <summary>
    /// 确保模型已下载并校验。已就绪直接返回路径；缺失则下载并校验。
    /// 失败时记录日志并返回 null（调用方降级能量 VAD）。
    /// </summary>
    public async Task<string?> EnsureModelAsync(CancellationToken cancellationToken)
    {
        if (IsReady())
            return ModelPath;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ModelPath)!);
            logger.LogInformation("Downloading Silero VAD model from GitHub.");
            await DownloadAsync(ModelUrl, ModelPath, cancellationToken);
            if (!await VerifyAsync(ModelPath, cancellationToken))
                throw new InvalidDataException("Silero VAD model failed SHA-256 verification.");
            return ModelPath;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Silero VAD model download failed; falling back to the energy-based VAD.");
            try { if (File.Exists(ModelPath)) File.Delete(ModelPath); } catch (IOException) { /* ignore */ }
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

    private static async Task<bool> VerifyAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
            return actual.Equals(ModelSha256, StringComparison.OrdinalIgnoreCase);
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
