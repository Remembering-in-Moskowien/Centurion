using Centurion.Models.Workflow;

namespace Centurion.Abstractions;

/// <summary>
/// Detected device capabilities, including platform, GPU, memory, and available inference backends.
/// </summary>
public sealed record DeviceCapabilities
{
    /// <summary>Platform identifier, such as "win-x64", "linux-x64", or "osx-arm64".</summary>
    public required string Platform { get; init; }

    /// <summary>Whether an NVIDIA GPU is present and CUDA is available.</summary>
    public required bool HasNvidiaGpu { get; init; }

    /// <summary>GPU name, or null if it could not be detected.</summary>
    public string? GpuName { get; init; }

    /// <summary>GPU memory in bytes, or 0 if it could not be detected.</summary>
    public long GpuMemoryBytes { get; init; }

    /// <summary>Available system memory in bytes.</summary>
    public required long SystemMemoryBytes { get; init; }

    /// <summary>Available inference backends in priority order; CPU is always last.</summary>
    public required IReadOnlyList<InferenceDevice> AvailableDevices { get; init; }

    /// <summary>Recommended inference device, used for automatic selection.</summary>
    public required InferenceDevice RecommendedDevice { get; init; }

    /// <summary>Human-readable device summary used in startup logs.</summary>
    public string DeviceSummary =>
        $"Platform: {Platform}; " +
        (HasNvidiaGpu
            ? $"GPU: {GpuName ?? "NVIDIA (CUDA_PATH)"} ({GpuMemoryBytes / 1024.0 / 1024.0 / 1024.0:F1} GB VRAM); "
            : GpuName is not null
                ? $"GPU: {GpuName} (non-NVIDIA); "
                : "GPU: none (CPU); ") +
        $"RAM: {SystemMemoryBytes / 1024.0 / 1024.0 / 1024.0:F1} GB; " +
        $"Recommended: {RecommendedDevice}";
}

/// <summary>
/// Detects GPUs (NVIDIA/CUDA and Vulkan), memory, and platform, then recommends
/// an inference device so tool binaries can download a matching GPU variant when needed.
/// </summary>
public interface IDeviceDetector
{
    /// <summary>
    /// Detects the current device capabilities; results are typically cached.
    /// </summary>
    DeviceCapabilities Detect();
}
