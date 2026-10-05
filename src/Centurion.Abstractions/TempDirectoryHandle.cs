using Centurion.Models.Console;
namespace Centurion.Abstractions;

/// <summary>
/// Handle for a temporary directory, implementing IDisposable and IAsyncDisposable.
/// </summary>
public class TempDirectoryHandle : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Full path to the temporary directory.
    /// </summary>
    public string Path { get; }
    private readonly bool _autoDelete;

    internal TempDirectoryHandle(string path, bool autoDelete = true)
    {
        Path = path;
        _autoDelete = autoDelete;
    }

    /// <summary>
    /// Disposes the handle and synchronously deletes the directory when auto-delete is enabled.
    /// </summary>
    public void Dispose()
    {
        if (_autoDelete)
            DeleteDirectory();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Asynchronously disposes the handle and deletes the directory when auto-delete is enabled.
    /// </summary>
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
            // Log the error without throwing so the workflow can continue.
            ConsoleServices.Output?.WriteWarning($"Failed to delete temp directory {Path}: {ex.Message}");
        }
    }

    private async Task DeleteDirectoryAsync()
    {
        if (!Directory.Exists(Path)) return;
        try
        {
            // Wrap the synchronous Directory.Delete call in a task.
            await Task.Run(() => Directory.Delete(Path, true));
        }
        catch (Exception ex)
        {
            ConsoleServices.Output?.WriteWarning($"Failed to delete temp directory {Path}: {ex.Message}");
        }
    }
}
