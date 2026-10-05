namespace Centurion.Abstractions;

/// <summary>
/// Manager responsible for creating and cleaning up temporary directories.
/// </summary>
public interface ITempDirectoryManager
{
    /// <summary>
    /// Creates a temporary directory and returns a disposable handle.
    /// </summary>
    /// <param name="prefix">Optional directory name prefix.</param>
    /// <returns>A handle that deletes the temporary directory when disposed.</returns>
    Task<TempDirectoryHandle> CreateTempDirectoryAsync(string? prefix = null);
}
