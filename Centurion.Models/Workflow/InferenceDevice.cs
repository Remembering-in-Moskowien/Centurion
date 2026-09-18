namespace Centurion.Models.Workflow;

/// <summary>
/// 推理设备类型。Auto 表示由系统自动检测推荐。
/// </summary>
public enum InferenceDevice
{
    Auto = 0,
    Cpu,
    Cuda,
    Vulkan,
    DirectMl
}
