using Centurion.Abstractions;

namespace Centurion.Core.Managers;

/// <summary>
/// 临时目录管理器：所有管道临时产物统一放置在程序根目录下的 temp 目录
/// （<see cref="DefaultBasePath"/>），便于集中查看与清理。
/// 默认模式下（未自定义根目录）首次构造时自动清除上次运行遗留的旧临时目录。
/// </summary>
public class TempDirectoryManager : ITempDirectoryManager
{
    /// <summary>程序根目录下统一临时目录的绝对路径。</summary>
    public static string DefaultBasePath => Path.Combine(AppContext.BaseDirectory, "temp");

    private readonly string _basePath;
    private readonly bool _autoDelete;
    private readonly List<TempDirectoryHandle> _handles = [];
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// 创建临时目录管理器。
    /// </summary>
    /// <param name="basePath">临时根目录；为 null 时使用程序根目录下的 temp（<see cref="DefaultBasePath"/>）。</param>
    /// <param name="autoDelete">句柄释放时是否自动删除对应目录，默认开启。</param>
    public TempDirectoryManager(string? basePath = null, bool autoDelete = true)
    {
        _basePath = basePath ?? DefaultBasePath;
        _autoDelete = autoDelete;

        // 默认根目录由程序独占：启动时清掉上次运行异常退出遗留的旧临时目录
        if (basePath is null)
            CleanupStaleDirectories();
    }

    /// <summary>
    /// 创建一个带前缀与唯一 GUID 名称的临时目录，并返回其句柄以便后续清理。
    /// </summary>
    /// <param name="prefix">目录名前缀；未提供时使用 "centurion_"。</param>
    /// <returns>指向新建临时目录的句柄。</returns>
    public async Task<TempDirectoryHandle> CreateTempDirectoryAsync(string? prefix = null)
    {
        prefix ??= "centurion_";
        var dirName = $"{prefix}{Guid.NewGuid():N}";
        var fullPath = Path.Combine(_basePath, dirName);

        Directory.CreateDirectory(fullPath);

        var handle = new TempDirectoryHandle(fullPath, _autoDelete);

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
            foreach (var handle in _handles) await handle.DisposeAsync();
            _handles.Clear();
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>删除默认临时根目录下的全部旧子目录（均为程序自身创建的临时目录）。</summary>
    private void CleanupStaleDirectories()
    {
        try
        {
            if (!Directory.Exists(_basePath))
                return;

            foreach (var directory in Directory.GetDirectories(_basePath))
            {
                try
                {
                    Directory.Delete(directory, true);
                }
                catch
                {
                    // 被占用的目录（如并行进程）跳过，下次启动再清理
                }
            }
        }
        catch
        {
            // 清理失败不影响程序启动
        }
    }
}
