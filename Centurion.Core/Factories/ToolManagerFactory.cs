using Centurion.Core.Abstractions.Factories;
using Centurion.Core.Managers;
using Centurion.Core.Models.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace Centurion.Core.Factories;

/// <summary>
/// 默认的 ToolManager 工厂实现。
/// </summary>
public class ToolManagerFactory(IServiceProvider serviceProvider) : IToolManagerFactory
{
    public ToolManager Create(string toolName)
    {
        // 每次调用创建新的 ToolManager 实例，确保隔离性
        var registry = serviceProvider.GetRequiredService<ToolRegistry>();
        return new ToolManager(toolName, registry, serviceProvider);
    }
}
