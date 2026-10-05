using Centurion.Abstractions;
using Centurion.Abstractions.Exceptions;
using Centurion.Abstractions.Utils;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Capabilities.Managers.Tools;

/// <summary>
/// IndexTTS-Rust manager: locates tools/indextts/indextts.exe (Windows platform).
/// The engine is a pure Rust prebuilt binary (8b-is/IndexTTS-Rust), using the same bundling
/// strategy as VSF: placed by the user or bundled with a release, with no auto-download; when
/// missing, returns null and hints at how to build/place it.
/// </summary>
public sealed class IndexTtsManager(
    IBinaryLocator binaryLocator,
    ILogger<IndexTtsManager> logger)
{
    private string? _resolvedDirectory;

    /// <summary>Whether the IndexTTS engine is available (the executable exists).</summary>
    public bool IsInstalled => LocateExecutable() is not null;

    /// <summary>
    /// Ensures the IndexTTS engine is available and returns the executable path; returns null
    /// when missing (no auto-download).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
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
