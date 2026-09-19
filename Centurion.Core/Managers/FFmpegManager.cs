using Centurion.Abstractions;
using FFMpegCore;

namespace Centurion.Core.Managers;

/// <summary>
/// 管理 FFmpeg 二进制可用性，并配置 FFMpegCore 全局选项，
/// 其中临时目录由 ITempDirectoryManager 统一管理。
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
    /// 确保 FFmpeg 可用，并配置全局选项（包括临时目录）
    /// </summary>
    public async Task CheckHealthAsync()
    {
        if (_isInitialized) return;

        // 1. 定位 ffmpeg 可执行文件
        var binName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        var ffmpegPath = _binaryLocator.Locate(binName, "tools", "ffmpeg");
        if (string.IsNullOrEmpty(ffmpegPath) || !File.Exists(ffmpegPath))
            throw new FileNotFoundException("FFmpeg executable not found.");

        var binaryFolder = Path.GetDirectoryName(ffmpegPath)!;

        // 2. 从 TempDirectoryManager 创建一个专用临时目录
        //    使用固定前缀 "ffmpeg_" 以便识别，目录会在程序退出时由管理器自动清理
        _tempDirHandle = await _tempDirManager.CreateTempDirectoryAsync("ffmpeg_");
        var tempDir = _tempDirHandle.Path;

        // 3. 配置 FFMpegCore 全局选项
        GlobalFFOptions.Configure(new FFOptions
        {
            BinaryFolder = binaryFolder,
            TemporaryFilesFolder = tempDir
        });

        _isInitialized = true;
    }

    // ---------- 资源释放 ----------
    /// <summary>
    /// 释放由本管理器占用的资源，包括回收专用的 FFmpeg 临时目录句柄。
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