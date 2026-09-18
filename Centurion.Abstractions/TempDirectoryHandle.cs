using Centurion.Models.Console;
using Centurion.Core.Infrastructure;

namespace Centurion.Abstractions;

/// <summary>
/// 临时目录句柄，实现 IDisposable 和 IAsyncDisposable
/// </summary>
public class TempDirectoryHandle : IDisposable, IAsyncDisposable
{
    public string Path { get; }
    private readonly bool _autoDelete;

    internal TempDirectoryHandle(string path, bool autoDelete = true)
    {
        Path = path;
        _autoDelete = autoDelete;
    }

    public void Dispose()
    {
        if (_autoDelete)
            DeleteDirectory();
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_autoDelete)
            await DeleteDirectoryAsync();
        GC.SuppressFinalize(this);
    }

    private void DeleteDirectory()
    {
        if (!Directory.Exists(Path)) return;
        try
        {
            Directory.Delete(Path, true);
        }
        catch (Exception ex)
        {
            // 记录日志但不抛出，避免中断流程
            ConsoleServices.Output?.WriteWarning($"Failed to delete temp directory {Path}: {ex.Message}");
        }
    }

    private async Task DeleteDirectoryAsync()
    {
        if (!Directory.Exists(Path)) return;
        try
        {
            // 异步删除（实际上 Directory.Delete 是同步的，这里封装为 Task）
            await Task.Run(() => Directory.Delete(Path, true));
        }
        catch (Exception ex)
        {
            ConsoleServices.Output?.WriteWarning($"Failed to delete temp directory {Path}: {ex.Message}");
        }
    }
}
