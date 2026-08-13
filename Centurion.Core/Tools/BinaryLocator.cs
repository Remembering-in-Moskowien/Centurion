using Centurion.Core.Exceptions;

namespace Centurion.Core.Tools;

/// <summary>跨平台二进制查找工具：本地目录 + PATH环境变量检索</summary>
internal static class BinaryLocator
{
    /// <summary>缓存已查找成功的二进制路径，避免重复遍历</summary>
    private static readonly Dictionary<string, string> BinaryCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 查找可执行文件
    /// </summary>
    /// <param name="binaryName">程序名（ffmpeg.exe/whisper-cli/aria2c）</param>
    /// <param name="localSearchRelativeDirs">程序目录下优先检索的子目录</param>
    /// <returns>完整二进制路径</returns>
    /// <exception cref="FileNotFoundException">未找到抛出</exception>
    public static string Locate(string binaryName, params string[] localSearchRelativeDirs)
    {
        // 命中缓存直接返回
        if (BinaryCache.TryGetValue(binaryName, out var cached) && File.Exists(cached))
            return cached;

        var baseDir = AppContext.BaseDirectory;
        var candidatePaths = localSearchRelativeDirs.Select(subDir => Path.Combine(baseDir, subDir, binaryName))
            .Select(full => Path.GetFullPath(full)).ToList();

        // 1. 拼接本地优先检索路径
        // tool文件夹寻找
        candidatePaths.Add(Path.Combine(baseDir, "tools", binaryName));
        // 程序根目录直接检索
        candidatePaths.Add(Path.Combine(baseDir, binaryName));
        // 上级目录兼容调试
        candidatePaths.Add(Path.GetFullPath(Path.Combine(baseDir, "..", binaryName)));

        // 2. 遍历本地候选
        foreach (var path in candidatePaths.Distinct())
        {
            if (!File.Exists(path)) continue;
            BinaryCache[binaryName] = path;
            return path;
        }

        // 3. 读取PATH环境变量，区分系统分隔符
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathEnv))
            throw new BinaryNotFoundException(Localization.Get("BinaryNotFound", binaryName), binaryName);

        var separator = OperatingSystem.IsWindows() ? ';' : ':';
        var envDirs = pathEnv.Split(separator)
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Distinct();

        foreach (var dir in envDirs)
        {
            var fullPath = Path.Combine(dir, binaryName);
            if (!File.Exists(fullPath)) continue;
            BinaryCache[binaryName] = fullPath;
            return fullPath;
        }

        // 全部路径均未找到
        throw new BinaryNotFoundException(Localization.Get("BinaryNotFound", binaryName), binaryName);
    }

    /// <summary>清空缓存，用于二进制文件替换后重载</summary>
    public static void ClearCache() => BinaryCache.Clear();
}