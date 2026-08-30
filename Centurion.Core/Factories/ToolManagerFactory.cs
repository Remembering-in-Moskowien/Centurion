using Centurion.Core.Abstractions.Factories;
using Centurion.Core.Managers;

namespace Centurion.Core.Factories;

/// <summary>
/// 默认的 ToolManager 工厂实现。
/// </summary>
public class ToolManagerFactory(IServiceProvider serviceProvider) : IToolManagerFactory
{
    public ToolManager Create(string toolName)
    {
        // 每次调用创建新的 ToolManager 实例，确保隔离性
        return new ToolManager(toolName, serviceProvider);
    }
}