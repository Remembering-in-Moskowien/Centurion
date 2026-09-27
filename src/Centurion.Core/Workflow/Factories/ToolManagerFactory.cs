using Centurion.Models.Workflow;
using Centurion.Abstractions;
using Centurion.Abstractions.Factories;
using Centurion.Models.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Centurion.Core.Capabilities.Managers.Tools;
namespace Centurion.Core.Workflow.Factories;

/// <summary>
/// 默认的 ToolManager 工厂实现。
/// </summary>
public class ToolManagerFactory(IServiceProvider serviceProvider) : IToolManagerFactory
{
    /// <summary>
    /// 每次调用都创建并返回一个全新的 <see cref="ToolManager"/> 实例，以保证各调用方之间的隔离性。
    /// </summary>
    /// <param name="toolName">要管理的外部工具名称。</param>
    /// <param name="device">推理设备；为 <see cref="InferenceDevice.Auto"/> 时由设备检测器自动推荐。</param>
    /// <returns>新创建的工具管理器实例。</returns>
    public ToolManager Create(string toolName, InferenceDevice device = InferenceDevice.Auto)
    {
        // 每次调用创建新的 ToolManager 实例，确保隔离性
        var registry = serviceProvider.GetRequiredService<ToolRegistry>();

        // Auto：由设备检测器自动推荐（NVIDIA CUDA → CUDA 构建等）
        if (device == InferenceDevice.Auto)
            device = serviceProvider.GetRequiredService<IDeviceDetector>().Detect().RecommendedDevice;

        return new ToolManager(toolName, registry, device, serviceProvider);
    }
}
