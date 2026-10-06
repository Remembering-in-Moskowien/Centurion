using Centurion.Core.Capabilities.Managers.Media;
using Centurion.Models.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Centurion.Tests.Core;

/// <summary>Model-missing errors: CheckHealthAsync throws ModelMissingException (with an install hint) when a model is missing, and no longer auto-downloads.</summary>
public sealed class ModelMissingTests
{
    private static readonly IServiceProvider EmptyServices =
        new ServiceCollection().BuildServiceProvider();

    [Fact]
    public async Task CheckHealth_SingleFileMissing_ThrowsWithInstallHint()
    {
        var manager = new ModelManager(
            "tiny",
            new Dictionary<string, ModelMeta>(StringComparer.OrdinalIgnoreCase)
            {
                ["tiny"] = new ModelMeta(
                    "ggml-tiny-never-exists.bin",
                    "https://example.invalid/ggml-tiny-never-exists.bin")
            },
            EmptyServices,
            categoryFolder: "whispercpp");

        var ex = await Assert.ThrowsAsync<ModelMissingException>(
            () => manager.CheckHealthAsync(default));
        Assert.Equal("tiny", ex.ModelName);
        Assert.Contains("Centurion models install tiny", ex.Message);
        Assert.Contains(ex.ExpectedPath, ex.Message);
    }

    [Fact]
    public async Task CheckHealth_DirectoryModelMissing_ThrowsWithInstallHint()
    {
        var manager = new ModelManager(
            "fake-dir",
            new Dictionary<string, ModelMeta>(StringComparer.OrdinalIgnoreCase)
            {
                ["fake-dir"] = new ModelMeta(
                    "https://example.invalid/fake-dir/resolve/main",
                    ["config.json", "model.bin"])
            },
            EmptyServices,
            categoryFolder: "fasterwhisper");

        var ex = await Assert.ThrowsAsync<ModelMissingException>(
            () => manager.CheckHealthAsync(default));
        // Content-addressed layout: the expected path is the hash-named location, and the
        // install hint lists the member files.
        Assert.Contains("<content-hash>", ex.ExpectedPath);
        Assert.Contains("Centurion models install fake-dir", ex.Message);
        Assert.Contains("config.json", ex.Message);
    }

    [Fact]
    public async Task CheckHealth_ManagementDisabled_DoesNotThrow()
    {
        var manager = new ModelManager(
            string.Empty,
            new Dictionary<string, ModelMeta>(),
            EmptyServices,
            "whispercpp");
        Assert.False(manager.ManagementEnabled);
        await manager.CheckHealthAsync(default); // should not throw
    }
}
