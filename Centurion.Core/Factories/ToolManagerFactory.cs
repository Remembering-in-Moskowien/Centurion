using Centurion.Models.Workflow;
using Centurion.Abstractions;
using Centurion.Abstractions.Factories;
using Centurion.Core.Managers;
using Centurion.Models.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace Centurion.Core.Factories;

/// <summary>
/// 默认的 ToolManager 工厂实现。
/// </summary>
public class ToolManagerFactory(IServiceProvider serviceProvider) : IToolManagerFactory
{
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
