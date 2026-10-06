namespace Centurion.Models.Workflow;

/// <summary>
/// Inference device type. Auto means the system automatically detects and recommends one.
/// </summary>
public enum InferenceDevice
{
    /// <summary>The system automatically detects and selects the most suitable inference device.</summary>
    Auto = 0,
    /// <summary>Force inference on CPU.</summary>
    Cpu,
    /// <summary>Use an NVIDIA CUDA GPU for inference.</summary>
    Cuda,
    /// <summary>Use a Vulkan-compatible GPU for inference.</summary>
    Vulkan,
    /// <summary>Use a DirectML-compatible GPU for inference.</summary>
    DirectMl
}
