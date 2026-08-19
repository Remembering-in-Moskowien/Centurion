using Centurion.Core.Exceptions;

namespace Centurion.Core.Abstractions;

/// <summary>
/// 二进制文件查找服务接口。
/// </summary>
public interface IBinaryLocator
{
    /// <summary>
    /// 查找可执行文件的完整路径。
    /// </summary>
    /// <param name="binaryName">程序名（如 ffmpeg.exe）</param>
    /// <param name="localSearchRelativeDirs">程序目录下优先检索的子目录</param>
    /// <returns>完整路径</returns>
    /// <exception cref="BinaryNotFoundException">未找到时抛出</exception>
    string Locate(string binaryName, params string[] localSearchRelativeDirs);

    /// <summary>清空缓存</summary>
    void ClearCache();
}