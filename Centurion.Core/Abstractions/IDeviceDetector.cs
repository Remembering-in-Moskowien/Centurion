namespace Centurion.Core.Abstractions;

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

/// <summary>
/// 设备能力检测结果：平台、GPU、内存与可用的推理后端。
/// </summary>
public sealed record DeviceCapabilities
{
    /// <summary>平台标识，如 "win-x64" / "linux-x64" / "osx-arm64"。</summary>
    public required string Platform { get; init; }

    /// <summary>是否存在 NVIDIA GPU（CUDA 可用）。</summary>
    public required bool HasNvidiaGpu { get; init; }

    /// <summary>GPU 名称（探测不到时为 null）。</summary>
    public string? GpuName { get; init; }

    /// <summary>显存字节数（探测不到时为 0）。</summary>
    public long GpuMemoryBytes { get; init; }

    /// <summary>系统可用内存字节数。</summary>
    public required long SystemMemoryBytes { get; init; }

    /// <summary>可用推理后端（按优先级排序，CPU 恒在末尾）。</summary>
    public required IReadOnlyList<InferenceDevice> AvailableDevices { get; init; }

    /// <summary>推荐的推理设备（自动选择时使用）。</summary>
    public required InferenceDevice RecommendedDevice { get; init; }

    /// <summary>人类可读的设备摘要（用于启动日志）。</summary>
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
/// 设备检测器：探测 GPU（NVIDIA/CUDA、Vulkan）、内存与平台，
/// 并给出推荐的推理设备，供工具二进制按需自动下载 GPU 变体。
/// </summary>
public interface IDeviceDetector
{
    /// <summary>
    /// 检测当前设备能力（结果通常缓存复用）。
    /// </summary>
    DeviceCapabilities Detect();
}
