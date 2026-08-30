using Centurion.Core.Managers;

namespace Centurion.Core.Abstractions.Factories;

/// <summary>
/// 工厂服务，用于按工具名称创建 ToolManager 实例。
/// </summary>
public interface IToolManagerFactory
{
    /// <summary>
    /// 创建指定工具的 ToolManager。
    /// </summary>
    /// <param name="toolName">工具名称（须在 ToolRegistry 中注册）</param>
    /// <returns>ToolManager 实例</returns>
    ToolManager Create(string toolName);
}