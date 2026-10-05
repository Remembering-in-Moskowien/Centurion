using Centurion.Models.Workflow;
using Centurion.Core.Capabilities.Managers.Tools;
namespace Centurion.Core.Workflow.Factories;

/// <summary>
/// Factory service for creating ToolManager instances by tool name.
/// </summary>
public interface IToolManagerFactory
{
    /// <summary>
    /// Creates a ToolManager for the specified tool.
    /// </summary>
    /// <param name="toolName">The tool name (must be registered in ToolRegistry).</param>
    /// <param name="device">
    /// The desired inference device; with <see cref="InferenceDevice.Auto"/> (the default) the system
    /// auto-detects and recommends a device. When the tool registry has a matching device variant it is
    /// selected automatically (e.g. the CUDA build of whisper.cpp).
    /// </param>
    /// <returns>A ToolManager instance.</returns>
    ToolManager Create(string toolName, InferenceDevice device = InferenceDevice.Auto);
}
