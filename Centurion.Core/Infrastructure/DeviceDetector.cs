// Centurion.Core/Infrastructure/DeviceDetector.cs

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Centurion.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Infrastructure;

/// <summary>
/// 设备检测器：探测 GPU（NVIDIA CUDA / Vulkan 候选）、系统内存与平台，
/// 推荐用于自动下载 GPU 变体工具（如 whisper.cpp CUDA 版）的推理设备。
/// </summary>
public sealed class DeviceDetector(ILogger<DeviceDetector> logger) : IDeviceDetector
{
    private DeviceCapabilities? _cached;

    public DeviceCapabilities Detect()
    {
        if (_cached is not null)
            return _cached;

        var platform = BuildPlatform();
        var hasNvidia = TryProbeNvidia(out var gpuName, out var gpuMemoryBytes);
        string? otherGpuName = null;
        var hasOtherGpu = !hasNvidia && TryProbeOtherGpu(out otherGpuName);
        var systemMemoryBytes = ProbeSystemMemory();

        var devices = new List<InferenceDevice>();
        if (hasNvidia)
            devices.Add(InferenceDevice.Cuda);
        if (hasOtherGpu && OperatingSystem.IsWindows())
            devices.Add(InferenceDevice.Vulkan); // 现代 Windows 驱动基本均支持 Vulkan
        if (OperatingSystem.IsWindows())
            devices.Add(InferenceDevice.DirectMl);
        devices.Add(InferenceDevice.Cpu);

        var recommended = devices.FirstOrDefault(d => d != InferenceDevice.Cpu, InferenceDevice.Cpu);

        _cached = new DeviceCapabilities
        {
            Platform = platform,
            HasNvidiaGpu = hasNvidia,
            GpuName = hasNvidia ? gpuName : (hasOtherGpu ? otherGpuName : null),
            GpuMemoryBytes = gpuMemoryBytes,
            SystemMemoryBytes = systemMemoryBytes,
            AvailableDevices = devices,
            RecommendedDevice = recommended
        };

        logger.LogInformation("Device detection: {Summary}", _cached.DeviceSummary);
        return _cached;
    }

    // ---------- 探测实现 ----------

    private static string BuildPlatform()
    {
        var os = OperatingSystem.IsWindows() ? "win"
            : OperatingSystem.IsLinux() ? "linux"
            : OperatingSystem.IsMacOS() ? "osx"
            : "unknown";
        var arch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            _ => "unknown"
        };
        return $"{os}-{arch}";
    }

    /// <summary>通过 nvidia-smi 探测 NVIDIA GPU（名称 + 显存，2s 超时）。</summary>
    private bool TryProbeNvidia(out string? gpuName, out long gpuMemoryBytes)
    {
        gpuName = null;
        gpuMemoryBytes = 0;

        try
        {
            var output = RunProbe("nvidia-smi", "--query-gpu=name,memory.total --format=csv,noheader", 2000);
            var line = output?.Split('\n').FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));
            if (string.IsNullOrWhiteSpace(line))
                throw new InvalidOperationException("nvidia-smi returned no GPU rows.");

            var parts = line.Split(',');
            gpuName = parts.Length > 0 ? parts[0].Trim() : "NVIDIA GPU";
            if (parts.Length > 1 && double.TryParse(parts[1].Trim().Split(' ')[0], out var miB))
                gpuMemoryBytes = (long)(miB * 1024 * 1024);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogDebug("nvidia-smi probe failed: {Message}", ex.Message);
        }

        // 兜底：CUDA 工具链已安装但 nvidia-smi 不在 PATH
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CUDA_PATH"))
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CUDA_HOME")))
        {
            gpuName = "NVIDIA (CUDA toolkit detected)";
            return true;
        }

        return false;
    }

    /// <summary>探测非 NVIDIA GPU（AMD/Intel 等，Vulkan 候选）。</summary>
    private bool TryProbeOtherGpu(out string? gpuName)
    {
        gpuName = null;
        if (!OperatingSystem.IsWindows())
            return false;

        try
        {
            var script = "Get-CimInstance Win32_VideoController | ForEach-Object { $_.Name }";
            var output = RunProbe("powershell.exe", $"-NoProfile -NonInteractive -Command \"{script}\"", 4000);
            var names = (output ?? string.Empty)
                .Split('\n')
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToList();
            if (names.Count == 0)
                return false;

            var nonNvidia = names.FirstOrDefault(n => !n.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase));
            if (nonNvidia is not null)
            {
                gpuName = nonNvidia;
                return true;
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug("GPU query failed: {Message}", ex.Message);
        }
        return false;
    }

    /// <summary>探测系统可用物理内存。</summary>
    private static long ProbeSystemMemory()
    {
        if (OperatingSystem.IsWindows())
            return ProbeWindowsMemory();

        try
        {
            return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        }
        catch
        {
            return 0;
        }
    }

    [SupportedOSPlatform("windows")]
    private static long ProbeWindowsMemory()
    {
        var status = new MemoryStatusEx();
        return GlobalMemoryStatusEx(status) ? (long)status.ullAvailPhys : 0;
    }

    /// <summary>运行探测命令并返回标准输出（超时自动终止）。</summary>
    private static string? RunProbe(string fileName, string arguments, int timeoutMs)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        if (!process.Start())
            return null;

        if (!process.WaitForExit(timeoutMs))
        {
            try { process.Kill(true); } catch { }
            return null;
        }
        return process.StandardOutput.ReadToEnd();
    }

    // ---------- Windows 内存 P/Invoke ----------

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;

        public MemoryStatusEx() => dwLength = (uint)Marshal.SizeOf(typeof(MemoryStatusEx));
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    [SupportedOSPlatform("windows")]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx lpBuffer);
}
