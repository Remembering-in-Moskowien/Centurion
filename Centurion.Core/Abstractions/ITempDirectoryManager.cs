// Centurion.Core/Abstractions/ITempDirectoryManager.cs

namespace Centurion.Core.Abstractions;

/// <summary>
/// 临时目录管理器，负责创建和清理临时目录
/// </summary>
public interface ITempDirectoryManager
{
    /// <summary>
    /// 创建一个新的临时目录，并返回一个可释放句柄
    /// </summary>
    /// <param name="prefix">目录名前缀（可选）</param>
    /// <returns>临时目录句柄，释放时自动删除目录</returns>
    Task<TempDirectoryHandle> CreateTempDirectoryAsync(string? prefix = null);
}
