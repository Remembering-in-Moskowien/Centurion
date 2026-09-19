namespace Centurion.Models.Workflow;

/// <summary>
/// 推理设备类型。Auto 表示由系统自动检测推荐。
/// </summary>
public enum InferenceDevice
{
    /// <summary>由系统自动检测并选择最合适的推理设备。</summary>
    Auto = 0,
    /// <summary>强制使用 CPU 推理。</summary>
    Cpu,
    /// <summary>使用 NVIDIA CUDA GPU 推理。</summary>
    Cuda,
    /// <summary>使用 Vulkan 兼容 GPU 推理。</summary>
    Vulkan,
    /// <summary>使用 DirectML 兼容 GPU 推理。</summary>
    DirectMl
}
