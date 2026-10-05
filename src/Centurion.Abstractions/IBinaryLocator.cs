using Centurion.Abstractions.Exceptions;

namespace Centurion.Abstractions;

/// <summary>
/// Service interface for locating executable files.
/// </summary>
public interface IBinaryLocator
{
    /// <summary>
    /// Locates the full path to an executable.
    /// </summary>
    /// <param name="binaryName">Executable name, such as ffmpeg.exe.</param>
    /// <param name="localSearchRelativeDirs">Subdirectories under the application directory to search first.</param>
    /// <returns>The full path.</returns>
    /// <exception cref="BinaryNotFoundException">Thrown when the executable cannot be found.</exception>
    string Locate(string binaryName, params string[] localSearchRelativeDirs);

    /// <summary>Clears the cache.</summary>
    void ClearCache();
}