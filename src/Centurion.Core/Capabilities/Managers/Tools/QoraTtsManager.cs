using Centurion.Abstractions;
using Centurion.Abstractions.Exceptions;
using Centurion.Abstractions.Utils;
using Centurion.Core.Operators.Download;
using Centurion.Core.Operators.Download.Request;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Capabilities.Managers.Tools;

/// <summary>
/// QORA-TTS manager (incordlabs/QORA-TTS-12Hz-1.7B, a pure-Rust Qwen3-TTS inference implementation).
/// Uses the same bundling strategy as VSF: the tools/qora-tts/ directory is self-contained
/// (exe + model.qora-tts weights + config files); missing files (including the 1.56GB Q4
/// weights) are auto-downloaded on first use via aria (multithreaded segmented download).
/// The engine requires the model file in the same directory as the exe (the exe auto-discovers
/// it), so it does not use the models/ registry directory.
/// </summary>
public sealed class QoraTtsManager(
    IBinaryLocator binaryLocator,
    IServiceProvider serviceProvider,
    ILogger<QoraTtsManager> logger)
{
    /// <summary>Base URL for QORA-TTS 1.7B release assets (v0.1.0).</summary>
    private const string ReleaseBase = "https://github.com/incordlabs/QORA-TTS-12Hz-1.7B/releases/download/v0.1.0-1.7B";

    /// <summary>All files required in the tool directory (including the 1.56GB model weight).</summary>
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

    /// <summary>Whether QORA-TTS is available (exe + model weight present).</summary>
    public bool IsInstalled =>
        LocateExe() is not null && File.Exists(Path.Combine(ToolsDir, "model.qora-tts"));

    /// <summary>
    /// Ensures QORA-TTS is available and returns the qora-tts.exe path; auto-downloads when
    /// missing (~1.56GB on first run).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The exe path; returns null when the download fails.</returns>
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
                // All files go through Downloader (which routes github.com sources through the
                // CENTURION_DOWNLOAD_PROXY mirror automatically); plain HttpClient cannot reach
                // github.com from CN networks (TLS reset).
                using var downloader = serviceProvider.GetRequiredService<Centurion.Core.Operators.Download.Downloader>();
                await downloader.ProcessAsync(new OperatorsRequest<AriaDownloadRequest>
                {
                    Payload = new AriaDownloadRequest
                    {
                        Url = url,
                        FullSavePath = savePath,
                        SplitThread = file == "model.qora-tts" ? 8 : 2,
                        ServerConnection = 8,
                        MaxRetry = 5,
                        ProgressRefreshMs = 100
                    }
                }, cancellationToken);
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

    /// <summary>The tools/qora-tts directory (under AppContext.BaseDirectory).</summary>
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
