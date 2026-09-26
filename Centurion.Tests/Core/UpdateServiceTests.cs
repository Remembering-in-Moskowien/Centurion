using System.IO.Compression;
using Centurion.Core.Capabilities.Update;using Xunit;

namespace Centurion.Tests.Core;

public sealed class UpdateServiceTests
{
    // ------------------------------------------------------------------
    // TryParseVersion
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("v0.2.0", 0, 2, 0)]
    [InlineData("0.1.0", 0, 1, 0)]
    [InlineData("V1.2.3", 1, 2, 3)]
    [InlineData("0.2.0+abc123", 0, 2, 0)]
    [InlineData("0.2.0-alpha.1", 0, 2, 0)]
    public void TryParseVersion_HandlesCommonTagFormats(string raw, int major, int minor, int build)
    {
        var ok = GitHubUpdateService.TryParseVersion(raw, out var version);

        Assert.True(ok);
        Assert.Equal(new Version(major, minor, build), version);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("latest")]
    [InlineData("abc")]
    public void TryParseVersion_RejectsGarbage(string? raw)
    {
        var ok = GitHubUpdateService.TryParseVersion(raw, out _);

        Assert.False(ok);
    }

    // ------------------------------------------------------------------
    // IsNewer
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("0.1.0", "v0.2.0", true)]
    [InlineData("0.2.0", "0.2.0", false)]
    [InlineData("0.3.0", "0.2.0", false)]
    [InlineData("1.0.0", "0.9.9", false)]
    [InlineData("0.2.0", "0.2.1", true)]
    public void IsNewer_ComparesSemantically(string local, string remote, bool expected)
    {
        Assert.Equal(expected, GitHubUpdateService.IsNewer(local, remote));
    }

    [Fact]
    public void IsNewer_FallsBackToStringComparison_WhenVersionUnparsable()
    {
        Assert.True(GitHubUpdateService.IsNewer("alpha", "beta"));
        Assert.False(GitHubUpdateService.IsNewer("zeta", "beta"));
    }

    // ------------------------------------------------------------------
    // MatchAsset
    // ------------------------------------------------------------------

    private static List<ReleaseAssetInfo> SampleAssets() =>
    [
        new("Centurion-win-x64.zip", "https://example.com/win-x64.zip", 100),
        new("Centurion-win-arm64.zip", "https://example.com/win-arm64.zip", 90),
        new("Centurion-linux-x64.zip", "https://example.com/linux-x64.zip", 80),
        new("checksums.txt", "https://example.com/checksums.txt", 1)
    ];

    [Fact]
    public void MatchAsset_PrefersExactRidName()
    {
        var name = GitHubUpdateService.MatchAsset(SampleAssets(), "win-x64", preferredName: null);

        Assert.Equal("Centurion-win-x64.zip", name);
    }

    [Fact]
    public void MatchAsset_RespectsPreferredName()
    {
        var name = GitHubUpdateService.MatchAsset(SampleAssets(), "linux-x64", "Centurion-win-arm64.zip");

        Assert.Equal("Centurion-win-arm64.zip", name);
    }

    [Fact]
    public void MatchAsset_FallsBackToFuzzyMatch()
    {
        var assets = new List<ReleaseAssetInfo>
        {
            new("Centurion-win-x64-full.zip", "https://example.com/a.zip", 100),
            new("other-win-x64.zip", "https://example.com/b.zip", 999),
        };

        // 精确名不存在时，取包含 rid 的最大 zip
        var name = GitHubUpdateService.MatchAsset(assets, "osx-arm64", preferredName: null);
        Assert.Null(name);

        var fuzzy = GitHubUpdateService.MatchAsset(assets, "win-x64", preferredName: null);
        Assert.Equal("other-win-x64.zip", fuzzy);
    }

    [Fact]
    public void MatchAsset_EmptyAssets_ReturnsNull()
    {
        Assert.Null(GitHubUpdateService.MatchAsset([], "win-x64", preferredName: null));
    }

    // ------------------------------------------------------------------
    // ExtractZipSafely
    // ------------------------------------------------------------------

    [Fact]
    public void ExtractZipSafely_ExtractsFilesAndDirectories()
    {
        var zipPath = Path.Combine(Path.GetTempPath(), $"centurion-update-{Guid.NewGuid():N}.zip");
        var destDir = Path.Combine(Path.GetTempPath(), $"centurion-extract-{Guid.NewGuid():N}");
        try
        {
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                var exe = zip.CreateEntry("Centurion.Cli.exe");
                using (var exeStream = exe.Open())
                {
                    exeStream.WriteByte(1);
                }

                var nested = zip.CreateEntry("tools/ffmpeg.txt");
                using (var nestedStream = new StreamWriter(nested.Open()))
                {
                    nestedStream.Write("hello");
                }
            }

            GitHubUpdateService.ExtractZipSafely(zipPath, destDir);

            Assert.True(File.Exists(Path.Combine(destDir, "Centurion.Cli.exe")));
            Assert.True(File.Exists(Path.Combine(destDir, "tools", "ffmpeg.txt")));
        }
        finally
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);
            if (Directory.Exists(destDir)) Directory.Delete(destDir, recursive: true);
        }
    }

    [Fact]
    public void ExtractZipSafely_RejectsZipSlipEntries()
    {
        var zipPath = Path.Combine(Path.GetTempPath(), $"centurion-evil-{Guid.NewGuid():N}.zip");
        var destDir = Path.Combine(Path.GetTempPath(), $"centurion-extract-{Guid.NewGuid():N}");
        try
        {
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                var evil = zip.CreateEntry("../evil.txt");
                using var evilStream = evil.Open();
                evilStream.WriteByte(1);
            }

            Assert.Throws<InvalidDataException>(() => GitHubUpdateService.ExtractZipSafely(zipPath, destDir));
            Assert.False(File.Exists(Path.Combine(Path.GetTempPath(), "evil.txt")));
        }
        finally
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);
            if (Directory.Exists(destDir)) Directory.Delete(destDir, recursive: true);
        }
    }
}
