using Centurion.Core.Pipeline.Operators;
using Xunit;

namespace Centurion.Tests;

public sealed class VocalSeparationTests
{
    [Fact]
    public void BuildArguments_IncludesModelStemsAndPaths()
    {
        var args = VocalSeparationOperator.BuildArguments(
            "htdemucs", "C:\\tmp\\pre.wav", "C:\\tmp\\out");

        Assert.Equal(["-m", "htdemucs", "-s", "vocals", "-o", "C:\\tmp\\out", "C:\\tmp\\pre.wav"], args);
    }

    [Fact]
    public void FindVocalsFile_FindsVocalsRecursively()
    {
        var root = Path.Combine(Path.GetTempPath(), $"vocalstest_{Guid.NewGuid():N}");
        try
        {
            // 模拟 demucs 输出布局：out/htdemucs/input/vocals.wav + no_vocals.wav
            var songDir = Path.Combine(root, "htdemucs", "song");
            Directory.CreateDirectory(songDir);
            File.WriteAllText(Path.Combine(songDir, "no_vocals.wav"), "x");
            var vocals = Path.Combine(songDir, "vocals.wav");
            File.WriteAllText(vocals, "y");

            var found = VocalSeparationOperator.FindVocalsFile(root);

            Assert.Equal(vocals, found);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void FindVocalsFile_ReturnsNullWhenMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"vocalstest_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "no_vocals.wav"), "x");

            Assert.Null(VocalSeparationOperator.FindVocalsFile(root));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("htdemucs", "htdemucs.safetensors")]
    [InlineData("htdemucs_6s", "htdemucs_6s.safetensors")]
    [InlineData("htdemucs-ft", "htdemucs_ft.safetensors")]
    [InlineData("HTDEMUCS", "htdemucs.safetensors")]
    [InlineData("unknown-model", null)]
    public void GetDemucsModelFileName_MapsKnownModels(string model, string? expected)
    {
        Assert.Equal(expected, VocalSeparationOperator.GetDemucsModelFileName(model));
    }

    [Theory]
    [InlineData(
        "https://huggingface.co/set-soft/audio_separation/resolve/main/Demucs/htdemucs.safetensors",
        "https://hf-mirror.com/set-soft/audio_separation/resolve/main/Demucs/htdemucs.safetensors")]
    [InlineData(
        "https://hf-mirror.com/set-soft/audio_separation/resolve/main/Demucs/htdemucs.safetensors",
        "https://hf-mirror.com/set-soft/audio_separation/resolve/main/Demucs/htdemucs.safetensors")]
    public void BuildMirrorUrl_RewritesOnlyOfficialHuggingFaceHost(string url, string expected)
    {
        Assert.Equal(expected, VocalSeparationOperator.BuildMirrorUrl(url));
    }

    [Fact]
    public void GetDemucsCacheDir_EndsWithDemucsRs()
    {
        var dir = VocalSeparationOperator.GetDemucsCacheDir();
        Assert.EndsWith("demucs-rs", dir, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DefaultModelBaseUrl_PointsToOfficialHuggingFace()
    {
        Assert.StartsWith("https://huggingface.co/", VocalSeparationOperator.DefaultModelBaseUrl);
        Assert.EndsWith("Demucs/", VocalSeparationOperator.DefaultModelBaseUrl);
    }
}
