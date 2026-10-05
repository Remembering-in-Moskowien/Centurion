using Centurion.Models.Workflow;
using Centurion.Abstractions;
using Centurion.Abstractions.Factories;
using Centurion.Models.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Centurion.Core.Capabilities.Managers.Tools;
namespace Centurion.Core.Workflow.Factories;

/// <summary>
/// The default ToolManager factory implementation.
/// </summary>
public class ToolManagerFactory(IServiceProvider serviceProvider) : IToolManagerFactory
{
    /// <summary>
    /// Creates and returns a brand-new <see cref="ToolManager"/> instance on every call to guarantee isolation between callers.
    /// </summary>
    /// <param name="toolName">The name of the external tool to manage.</param>
    /// <param name="device">The inference device; when <see cref="InferenceDevice.Auto"/> the device detector recommends one automatically.</param>
    /// <returns>The newly created tool manager instance.</returns>
    public ToolManager Create(string toolName, InferenceDevice device = InferenceDevice.Auto)
    {
        // Create a fresh ToolManager instance on each call to guarantee isolation
        var registry = serviceProvider.GetRequiredService<ToolRegistry>();

        // Auto: the device detector recommends one automatically (NVIDIA CUDA -> CUDA build, etc.)
        if (device == InferenceDevice.Auto)
            device = serviceProvider.GetRequiredService<IDeviceDetector>().Detect().RecommendedDevice;

        return new ToolManager(toolName, registry, device, serviceProvider);
    }
}
