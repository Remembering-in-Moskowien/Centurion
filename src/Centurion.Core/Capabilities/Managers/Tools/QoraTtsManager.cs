using Centurion.Abstractions;
using Centurion.Abstractions.Exceptions;
using Centurion.Abstractions.Utils;
using Centurion.Core.Operators.Download;
using Centurion.Core.Operators.Download.Request;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Capabilities.Managers.Tools;

/// <summary>
/// QORA-TTS 管理器（incordlabs/QORA-TTS-12Hz-1.7B，纯 Rust 的 Qwen3-TTS 推理实现）。
/// 与 VSF 相同的捆绑策略：工具目录 tools/qora-tts/ 自包含（exe + model.qora-tts 权重 + 配置文件），
/// 首次使用自动下载缺失文件（含 1.56GB Q4 权重），下载走 aria（多线程分片）。
/// 引擎要求模型文件与 exe 同目录（exe 自动发现），因此不走 models/ 注册表目录。
/// </summary>
public sealed class QoraTtsManager(
    IBinaryLocator binaryLocator,
    IServiceProvider serviceProvider,
    ILogger<QoraTtsManager> logger)
{
    /// <summary>QORA-TTS 1.7B release 资产基址（v0.1.0）。</summary>
    private const string ReleaseBase = "https://github.com/incordlabs/QORA-TTS-12Hz-1.7B/releases/download/v0.1.0-1.7B";

    /// <summary>工具目录下需要的全部文件（含 1.56GB 模型权重）。</summary>
    private static readonly string[] RequiredFiles =
    [
        "qora-tts.exe",
        "config.json",
        "merges.txt",
        "model.qora-tts",
        "tokenizer.json",
        "tokenizer_config.json",
        "vocab.json"
    ];

    private string? _toolsDir;
    private string? _resolvedExe;

    /// <summary>QORA-TTS 是否已可用（exe + 模型权重齐备）。</summary>
    public bool IsInstalled =>
        LocateExe() is not null && File.Exists(Path.Combine(ToolsDir, "model.qora-tts"));

    /// <summary>
    /// 确保 QORA-TTS 可用，返回 qora-tts.exe 路径；缺失则自动下载（首次约 1.56GB）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>exe 路径；下载失败返回 null。</returns>
    public async Task<string?> EnsureInstalledAsync(CancellationToken cancellationToken)
    {
        var existing = LocateExe();
        if (existing is not null && File.Exists(Path.Combine(ToolsDir, "model.qora-tts")))
            return existing;

        Directory.CreateDirectory(ToolsDir);
        var missing = RequiredFiles.Where(f => !File.Exists(Path.Combine(ToolsDir, f))).ToList();
        if (missing.Count > 0)
        {
            logger.LogWarning(
                "QORA-TTS incomplete in {Dir}; downloading {MissingCount} file(s) (incl. ~1.56GB model weight) from release ...",
                ToolsDir, missing.Count);
        }

        foreach (var file in missing)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var savePath = Path.Combine(ToolsDir, file);
            var url = ReleaseBase + "/" + file;
            try
            {
                if (file == "model.qora-tts")
                {
                    // 大文件走 aria 多线程分片下载
                    using var downloader = serviceProvider.GetRequiredService<Centurion.Core.Operators.Download.Downloader>();
                    await downloader.ProcessAsync(new OperatorsRequest<AriaDownloadRequest>
                    {
                        Payload = new AriaDownloadRequest
                        {
                            Url = url,
                            FullSavePath = savePath,
                            SplitThread = 8,
                            ServerConnection = 8,
                            MaxRetry = 5,
                            ProgressRefreshMs = 100
                        }
                    }, cancellationToken);
                }
                else
                {
                    using var client = serviceProvider.GetRequiredService<HttpClient>();
                    using var resp = await client.GetStreamAsync(url, cancellationToken);
                    await using var fs = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None);
                    await resp.CopyToAsync(fs, cancellationToken);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
            {
                logger.LogWarning("QORA-TTS download failed for {File}: {Error}", file, ex.Message);
                return null;
            }
            logger.LogInformation("QORA-TTS downloaded {File} -> {Dir}", file, ToolsDir);
        }

        var exe = LocateExe();
        if (exe is null)
        {
            logger.LogWarning("QORA-TTS downloaded but qora-tts.exe still cannot be located.");
            return null;
        }
        return exe;
    }

    /// <summary>tools/qora-tts 目录（AppContext.BaseDirectory 下）。</summary>
    private string ToolsDir =>
        _toolsDir ??= Path.Combine(AppContext.BaseDirectory, "tools", "qora-tts");

    private string? LocateExe()
    {
        if (_resolvedExe is { } cached && File.Exists(cached))
            return cached;

        try
        {
            _resolvedExe = binaryLocator.Locate("qora-tts.exe", "tools/qora-tts");
            return _resolvedExe;
        }
        catch (BinaryNotFoundException)
        {
            return null;
        }
    }
}
