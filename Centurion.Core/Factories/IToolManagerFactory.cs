using Centurion.Models.Workflow;
using Centurion.Core.Managers;

namespace Centurion.Core.Factories;

/// <summary>
/// 工厂服务，用于按工具名称创建 ToolManager 实例。
/// </summary>
public interface IToolManagerFactory
{
    /// <summary>
    /// 创建指定工具的 ToolManager。
    /// </summary>
    /// <param name="toolName">工具名称（须在 ToolRegistry 中注册）</param>
    /// <param name="device">
    /// 期望的推理设备；<see cref="InferenceDevice.Auto"/>（默认）时由系统自动检测推荐设备。
    /// 工具注册表存在对应设备变体时自动选用（如 whisper.cpp 的 CUDA 构建）。
    /// </param>
    /// <returns>ToolManager 实例</returns>
    ToolManager Create(string toolName, InferenceDevice device = InferenceDevice.Auto);
}
