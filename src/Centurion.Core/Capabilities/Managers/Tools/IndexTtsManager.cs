using Centurion.Abstractions;
using Centurion.Abstractions.Exceptions;
using Centurion.Abstractions.Utils;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Capabilities.Managers.Tools;

/// <summary>
/// IndexTTS-Rust 管理器：定位 tools/indextts/indextts.exe（Windows 平台）。
/// 引擎为纯 Rust 预编译二进制（8b-is/IndexTTS-Rust），与 VSF 相同的捆绑策略：
/// 由用户放置或随发布捆绑，无自动下载；缺失时返回 null 并提示构建/放置方法。
/// </summary>
public sealed class IndexTtsManager(
    IBinaryLocator binaryLocator,
    ILogger<IndexTtsManager> logger)
{
    private string? _resolvedDirectory;

    /// <summary>IndexTTS 引擎是否已可用（可执行文件存在）。</summary>
    public bool IsInstalled => LocateExecutable() is not null;

    /// <summary>
    /// 确保 IndexTTS 引擎可用，返回可执行文件路径；缺失返回 null（不自动下载）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task<string?> EnsureInstalledAsync(CancellationToken cancellationToken)
    {
        var exe = LocateExecutable();
        if (exe is null)
        {
            logger.LogWarning(
                "IndexTTS engine not found under tools/indextts/. Build it from 8b-is/IndexTTS-Rust " +
                "(cargo build --release) and place indextts.exe there, or wait for a bundled release.");
        }
        return Task.FromResult(exe);
    }

    private string? LocateExecutable()
    {
        if (_resolvedDirectory is { } cached && File.Exists(Path.Combine(cached, "indextts.exe")))
            return Path.Combine(cached, "indextts.exe");

        try
        {
            var exe = binaryLocator.Locate("indextts.exe", "tools/indextts");
            _resolvedDirectory = Path.GetDirectoryName(exe);
            return exe;
        }
        catch (BinaryNotFoundException)
        {
            return null;
        }
    }
}
