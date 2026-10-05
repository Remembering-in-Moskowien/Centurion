using Centurion.Abstractions;

namespace Centurion.Core.Capabilities.Managers.Runtime;

/// <summary>
/// Temp-directory manager: all pipeline temp artifacts are placed under the temp directory in the
/// application root (<see cref="DefaultBasePath"/>), for centralized inspection and cleanup.
/// In the default mode (custom root not set), stale temp directories left by previous runs are
/// automatically cleared on first construction.
/// </summary>
public class TempDirectoryManager : ITempDirectoryManager
{
    /// <summary>Absolute path to the unified temp directory under the application root.</summary>
    public static string DefaultBasePath => Path.Combine(AppContext.BaseDirectory, "temp");

    private readonly string _basePath;
    private readonly bool _autoDelete;
    private readonly List<TempDirectoryHandle> _handles = [];
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// Creates a temp-directory manager.
    /// </summary>
    /// <param name="basePath">Temp root directory; when null, uses the temp directory under the application root (<see cref="DefaultBasePath"/>).</param>
    /// <param name="autoDelete">Whether to automatically delete the corresponding directory when the handle is disposed; enabled by default.</param>
    public TempDirectoryManager(string? basePath = null, bool autoDelete = true)
    {
        _basePath = basePath ?? DefaultBasePath;
        _autoDelete = autoDelete;

        // The default root is owned exclusively by the application: on startup, clear stale temp
        // directories left by a previous run that exited abnormally.
        if (basePath is null)
            CleanupStaleDirectories();
    }

    /// <summary>
    /// Creates a temp directory with a prefix and a unique GUID name, and returns its handle for
    /// later cleanup.
    /// </summary>
    /// <param name="prefix">Directory name prefix; defaults to "centurion_" when not provided.</param>
    /// <returns>A handle pointing to the newly created temp directory.</returns>
    public async Task<TempDirectoryHandle> CreateTempDirectoryAsync(string? prefix = null)
    {
        prefix ??= "centurion_";
        var dirName = $"{prefix}{Guid.NewGuid():N}";
        var fullPath = Path.Combine(_basePath, dirName);

        Directory.CreateDirectory(fullPath);

        var handle = new TempDirectoryHandle(fullPath, _autoDelete);

        // Register it for global cleanup (optional)
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
    /// Cleans up all registered temp directories (called when the application exits).
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

    /// <summary>Deletes all stale subdirectories under the default temp root (all are temp directories created by the application itself).</summary>
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
                    // Skip directories in use (e.g. by parallel processes); they will be cleaned up on next startup.
                }
            }
        }
        catch
        {
            // A cleanup failure must not prevent the application from starting.
        }
    }
}
