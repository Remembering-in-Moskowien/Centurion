using Centurion.Abstractions;
using Centurion.Abstractions.Exceptions;

// localization removed; strings hard-coded

namespace Centurion.Core.Utils.Infrastructure;

/// <summary>
/// Cross-platform binary locator: searches local directories and the PATH environment variable, with DI and localization support.
/// </summary>
public class BinaryLocator() : IBinaryLocator
{
    // localization removed
    private readonly Dictionary<string, string> _binaryCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Locates an executable by name: first searches local relative directories and the app base directory, then falls back to the PATH environment variable; hits are cached.
    /// </summary>
    /// <param name="binaryName">The executable file name.</param>
    /// <param name="localSearchRelativeDirs">Subdirectories to search first, relative to the app base directory.</param>
    /// <returns>The full path of the located executable.</returns>
    /// <exception cref="BinaryNotFoundException">Thrown when the binary is found neither locally nor on PATH.</exception>
    public string Locate(string binaryName, params string[] localSearchRelativeDirs)
    {
        // Cache hit: return directly
        if (_binaryCache.TryGetValue(binaryName, out var cached) && File.Exists(cached))
            return cached;

        var baseDir = AppContext.BaseDirectory;
        var candidatePaths = localSearchRelativeDirs
            .Select(subDir => Path.Combine(baseDir, subDir, binaryName))
            .Select(full => Path.GetFullPath(full))
            .ToList();

        // 1. Build local priority search paths
        candidatePaths.Add(Path.Combine(baseDir, "tools", binaryName));
        candidatePaths.Add(Path.Combine(baseDir, binaryName));
        candidatePaths.Add(Path.GetFullPath(Path.Combine(baseDir, "..", binaryName)));

        // 2. Walk local candidates
        foreach (var path in candidatePaths.Distinct())
        {
            if (!File.Exists(path)) continue;
            _binaryCache[binaryName] = path;
            return path;
        }

        // 3. Read the PATH environment variable
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathEnv))
            throw new BinaryNotFoundException($"Binary '{binaryName}' not found.", binaryName);

        var separator = OperatingSystem.IsWindows() ? ';' : ':';
        var envDirs = pathEnv.Split(separator)
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Distinct();

        foreach (var dir in envDirs)
        {
            var fullPath = Path.Combine(dir, binaryName);
            if (!File.Exists(fullPath)) continue;
            _binaryCache[binaryName] = fullPath;
            return fullPath;
        }

        throw new BinaryNotFoundException(
            $"Binary '{binaryName}' not found.", binaryName);
    }

    /// <summary>
    /// Clears cached binary lookup results.
    /// </summary>
    public void ClearCache()
    {
        _binaryCache.Clear();
    }
}