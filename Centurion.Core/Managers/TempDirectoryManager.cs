using Centurion.Core.Abstractions;

namespace Centurion.Core.Tools;

/// <summary>
/// 临时目录管理器实现
/// </summary>
public class TempDirectoryManager(string? basePath = null, bool autoDelete = true) : ITempDirectoryManager
{
    private readonly string _basePath = basePath ?? Path.GetTempPath();
    private readonly List<TempDirectoryHandle> _handles = [];
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<TempDirectoryHandle> CreateTempDirectoryAsync(string? prefix = null)
    {
        prefix ??= "centurion_";
        var dirName = $"{prefix}{Guid.NewGuid():N}";
        var fullPath = Path.Combine(_basePath, dirName);

        Directory.CreateDirectory(fullPath);

        var handle = new TempDirectoryHandle(fullPath, autoDelete);

        // 注册以便全局清理（可选）
        await _lock.WaitAsync();
        try
        {
            _handles.Add(handle);
        }
        finally
        {
            _lock.Release();
        }

        return handle;
    }

    /// <summary>
    /// 清理所有已注册的临时目录（在程序退出时调用）
    /// </summary>
    public async Task CleanupAllAsync()
    {
        await _lock.WaitAsync();
        try
        {
            foreach (var handle in _handles)
            {
                await handle.DisposeAsync();
            }
            _handles.Clear();
        }
        finally
        {
            _lock.Release();
        }
    }
}