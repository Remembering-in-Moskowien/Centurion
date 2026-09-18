using Centurion.Models.Workflow;
using Centurion.Abstractions;
using Centurion.Core.Managers;
using Centurion.Models.Metadata;
using Xunit;

namespace Centurion.Tests;

public sealed class DeviceVariantTests
{
    private static ToolMeta BuildMeta(Dictionary<string, ToolVariant>? variants = null) => new()
    {
        ToolName = "testtool",
        DownloadUrl = "https://example.com/base.zip",
        ArchiveType = "zip",
        ExecutableRelativePath = "tool.exe",
        Version = "v1.0",
        Variants = variants
    };

    [Fact]
    public void ResolveVariant_NoVariants_UsesBaseFields()
    {
        var meta = BuildMeta();

        var (url, archive, exe, description) = ToolManager.ResolveVariant(meta, InferenceDevice.Cuda);

        Assert.Equal("https://example.com/base.zip", url);
        Assert.Equal("zip", archive);
        Assert.Equal("tool.exe", exe);
        Assert.Null(description);
    }

    [Fact]
    public void ResolveVariant_ExactDeviceMatch_UsesCudaVariant()
    {
        var meta = BuildMeta(new Dictionary<string, ToolVariant>
        {
            ["cuda"] = new()
            {
                DownloadUrl = "https://example.com/cuda.zip",
                ExecutableRelativePath = "cuda-tool.exe",
                Description = "CUDA build"
            }
        });

        var (url, archive, exe, description) = ToolManager.ResolveVariant(meta, InferenceDevice.Cuda);

        Assert.Equal("https://example.com/cuda.zip", url);
        Assert.Equal("zip", archive); // 未覆盖字段回退基础值
        Assert.Equal("cuda-tool.exe", exe);
        Assert.Equal("CUDA build", description);
    }

    [Fact]
    public void ResolveVariant_NoExactMatch_FallsBackToDefaultVariant()
    {
        var meta = BuildMeta(new Dictionary<string, ToolVariant>
        {
            ["default"] = new() { DownloadUrl = "https://example.com/default.zip" }
        });

        var (url, archive, exe, _) = ToolManager.ResolveVariant(meta, InferenceDevice.Cuda);

        Assert.Equal("https://example.com/default.zip", url);
        Assert.Equal("zip", archive);
        Assert.Equal("tool.exe", exe);
    }

    [Fact]
    public void ResolveVariant_NoExactNoDefault_UsesBaseFields()
    {
        var meta = BuildMeta(new Dictionary<string, ToolVariant>
        {
            ["vulkan"] = new() { DownloadUrl = "https://example.com/vulkan.zip" }
        });

        var (url, archive, exe, _) = ToolManager.ResolveVariant(meta, InferenceDevice.Cuda);

        Assert.Equal("https://example.com/base.zip", url);
        Assert.Equal("zip", archive);
        Assert.Equal("tool.exe", exe);
    }

    [Fact]
    public void ResolveVariant_CpuDevice_UsesCpuKey()
    {
        var meta = BuildMeta(new Dictionary<string, ToolVariant>
        {
            ["cpu"] = new() { DownloadUrl = "https://example.com/cpu.zip" }
        });

        var (url, _, _, _) = ToolManager.ResolveVariant(meta, InferenceDevice.Cpu);
        Assert.Equal("https://example.com/cpu.zip", url);
    }

    [Fact]
    public void DefaultRegistry_WhisperCpp_HasCudaVariant()
    {
        var whisper = ToolRegistry.Default.Tools["whispercpp"];

        Assert.NotNull(whisper.Variants);
        Assert.True(whisper.Variants!.ContainsKey("cuda"));

        var (url, _, exe, description) = ToolManager.ResolveVariant(whisper, InferenceDevice.Cuda);
        Assert.Contains("cublas", url);
        Assert.Equal("whisper-cli.exe", exe);
        Assert.NotNull(description);
    }

    [Fact]
    public void DefaultRegistry_WhisperCpp_CpuUsesBaseUrl()
    {
        var whisper = ToolRegistry.Default.Tools["whispercpp"];

        var (url, _, _, _) = ToolManager.ResolveVariant(whisper, InferenceDevice.Cpu);
        Assert.Contains("whisper-bin-x64.zip", url);
    }
}
