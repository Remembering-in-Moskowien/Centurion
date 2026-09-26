using Centurion.Abstractions;
using Xunit;

namespace Centurion.Tests.Cli;

/// <summary>第 6 步 CLI 体验：配置文件加载/环境变量覆盖、退出码、init 配方。</summary>
public sealed class CenturionConfigTests
{
    [Fact]
    public void Load_EmptyConfig_ReturnsDefaults()
    {
        var config = CenturionConfig.Load();
        Assert.NotNull(config);
        Assert.Null(config.Profile);
    }

    [Fact]
    public void Load_InvalidJson_FallsBackToDefaults()
    {
        var dir = Path.Combine(Path.GetTempPath(), "centurion-cfg-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "centurion.config.json");
            File.WriteAllText(path, "{ not valid json !!");
            // 显式路径加载：非法 JSON 静默回退默认
            var config = CenturionConfig.Load(path);
            Assert.Null(config.Profile);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Load_ValidFile_ParsesValues()
    {
        var dir = Path.Combine(Path.GetTempPath(), "centurion-cfg-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "centurion.config.json");
            File.WriteAllText(path, """{"profile":"offline","outputFormat":"ass"}""");
            var config = CenturionConfig.Load(path);
            Assert.Equal("offline", config.Profile);
            Assert.Equal("ass", config.OutputFormat);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}

/// <summary>标准化退出码契约。</summary>
public sealed class ExitCodesTests
{
    [Fact]
    public void ExitCodes_Contract_Stable()
    {
        Assert.Equal(0, ExitCodes.Success);
        Assert.Equal(1, ExitCodes.Failure);
        Assert.Equal(2, ExitCodes.Usage);
        Assert.Equal(130, ExitCodes.Cancelled);
    }
}
