using Centurion.Core.Pipeline.Operators;
using Xunit;

namespace Centurion.Tests;

public sealed class VocalSeparationTests
{
    [Fact]
    public void BuildArguments_IncludesModelTwoStemsAndPaths()
    {
        var args = VocalSeparationOperator.BuildArguments(
            "htdemucs", "C:\\tmp\\pre.wav", "C:\\tmp\\out");

        Assert.Contains("-n htdemucs", args);
        Assert.Contains("--two-stems vocals", args);
        Assert.Contains("-o \"C:\\tmp\\out\"", args);
        Assert.Contains("\"C:\\tmp\\pre.wav\"", args);
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
}
