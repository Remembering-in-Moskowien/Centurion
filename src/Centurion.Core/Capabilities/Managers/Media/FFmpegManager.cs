using Centurion.Abstractions;
using FFMpegCore;
using Centurion.Core.Capabilities.Managers.Runtime;
namespace Centurion.Core.Capabilities.Managers.Media;

/// <summary>
/// Manages FFmpeg binary availability and configures FFMpegCore global options;
/// the temporary directory is centrally managed by ITempDirectoryManager.
/// </summary>
public class FFmpegManager(IBinaryLocator binaryLocator, ITempDirectoryManager tempDirManager)
    : IAsyncDisposable
{
    private readonly IBinaryLocator _binaryLocator = binaryLocator ?? throw new ArgumentNullException(nameof(binaryLocator));
    private readonly ITempDirectoryManager _tempDirManager = tempDirManager ?? throw new ArgumentNullException(nameof(tempDirManager));
    private TempDirectoryHandle? _tempDirHandle;
    private bool _isInitialized;
    private bool _disposed;

    /// <summary>
    /// Ensures FFmpeg is available and configures global options (including the temp directory).
    /// </summary>
    public async Task CheckHealthAsync()
    {
        if (_isInitialized) return;

        // 1. Locate the ffmpeg executable
        var binName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        var ffmpegPath = _binaryLocator.Locate(binName, "tools", "ffmpeg");
        if (string.IsNullOrEmpty(ffmpegPath) || !File.Exists(ffmpegPath))
            throw new FileNotFoundException("FFmpeg executable not found.");

        var binaryFolder = Path.GetDirectoryName(ffmpegPath)!;

        // 2. Create a dedicated temp directory from TempDirectoryManager.
        //    Uses the fixed prefix "ffmpeg_" for identification; the directory is cleaned up
        //    automatically by the manager when the application exits.
        _tempDirHandle = await _tempDirManager.CreateTempDirectoryAsync("ffmpeg_");
        var tempDir = _tempDirHandle.Path;

        // 3. Configure FFMpegCore global options
        GlobalFFOptions.Configure(new FFOptions
        {
            BinaryFolder = binaryFolder,
            TemporaryFilesFolder = tempDir
        });

        _isInitialized = true;
    }

    // ---------- Resource cleanup ----------
    /// <summary>
    /// Releases resources held by this manager, including disposing the dedicated FFmpeg temp directory handle.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_tempDirHandle != null)
        {
            await _tempDirHandle.DisposeAsync();
            _tempDirHandle = null;
        }
    }
}