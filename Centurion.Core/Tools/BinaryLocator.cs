using Centurion.Core.Exceptions;
// localization removed; strings hard-coded

namespace Centurion.Core.Tools;

/// <summary>
/// 跨平台二进制查找工具：本地目录 + PATH 环境变量检索，支持 DI 和本地化。
/// </summary>
public class BinaryLocator() : IBinaryLocator
{
    // localization removed
    private readonly Dictionary<string, string> _binaryCache = new(StringComparer.OrdinalIgnoreCase);

    public string Locate(string binaryName, params string[] localSearchRelativeDirs)
    {
        // 命中缓存直接返回
        if (_binaryCache.TryGetValue(binaryName, out var cached) && File.Exists(cached))
            return cached;

        var baseDir = AppContext.BaseDirectory;
        var candidatePaths = localSearchRelativeDirs
            .Select(subDir => Path.Combine(baseDir, subDir, binaryName))
            .Select(full => Path.GetFullPath(full))
            .ToList();

        // 1. 拼接本地优先检索路径
        candidatePaths.Add(Path.Combine(baseDir, "tools", binaryName));
        candidatePaths.Add(Path.Combine(baseDir, binaryName));
        candidatePaths.Add(Path.GetFullPath(Path.Combine(baseDir, "..", binaryName)));

        // 2. 遍历本地候选
        foreach (var path in candidatePaths.Distinct())
        {
            if (!File.Exists(path)) continue;
            _binaryCache[binaryName] = path;
            return path;
        }

        // 3. 读取 PATH 环境变量
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathEnv))
            throw new BinaryNotFoundException(string.Format("Binary '{0}' not found.", binaryName), binaryName);

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
            string.Format("Binary '{0}' not found.", binaryName), binaryName);
    }

    public void ClearCache()
    {
        _binaryCache.Clear();
    }
}