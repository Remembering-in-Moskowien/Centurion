using Centurion.Models.Workflow;

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Centurion.Abstractions;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Capabilities.Infrastructure;

/// <summary>
/// Device detector: probes the GPU (NVIDIA CUDA / Vulkan candidates), system memory
/// and platform, and recommends an inference device for auto-downloading GPU-variant
/// tools (e.g. the CUDA build of whisper.cpp).
/// </summary>
public sealed class DeviceDetector(ILogger<DeviceDetector> logger) : IDeviceDetector
{
    private DeviceCapabilities? _cached;

    /// <summary>
    /// Probes the local GPU, system memory and platform capabilities and recommends an
    /// inference device; the result is cached after the first probe, and subsequent calls
    /// return the cached value.
    /// </summary>
    /// <returns>An object describing the local device capabilities and the recommended inference device.</returns>
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
            devices.Add(InferenceDevice.Vulkan); // Modern Windows drivers essentially all support Vulkan
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

    // ---------- Probe implementations ----------

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

    /// <summary>Probes for an NVIDIA GPU via nvidia-smi (name + VRAM, 2s timeout).</summary>
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
        catch
        {
            // Do not log system exception messages (non-UTF-8 system text would pollute the
            // log with mojibake); just mark the probe as failed
            logger.LogDebug("nvidia-smi probe failed; NVIDIA GPU detection skipped.");
        }

        // Fallback: the CUDA toolchain is installed but nvidia-smi is not on PATH
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CUDA_PATH"))
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CUDA_HOME")))
        {
            gpuName = "NVIDIA (CUDA toolkit detected)";
            return true;
        }

        return false;
    }

    /// <summary>Probes for a non-NVIDIA GPU (AMD/Intel, etc.; Vulkan candidates).</summary>
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

    /// <summary>Probes available physical system memory.</summary>
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

    /// <summary>Runs a probe command and returns its standard output (killed automatically on timeout).</summary>
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

    // ---------- Windows memory P/Invoke ----------

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
