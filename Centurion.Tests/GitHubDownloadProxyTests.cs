using Centurion.Core.Utils;
using Xunit;

namespace Centurion.Tests;

/// <summary>
/// GitHub 下载加速代理（镜像候选链）单元测试。
/// </summary>
public class GitHubDownloadProxyTests
{
    [Fact]
    public void CandidateUrls_PrefersUserMirror_ThenOriginal()
    {
        GitHubDownloadProxy.Disabled = false;
        GitHubDownloadProxy.ProxyPrefix = "https://my-mirror.example/";
        try
        {
            var url = "https://github.com/owner/repo/releases/download/v1.0/pkg.zip";
            var candidates = GitHubDownloadProxy.CandidateUrls(url).ToList();

            Assert.Equal(2, candidates.Count);
            Assert.Equal("https://my-mirror.example/" + url, candidates[0]);
            Assert.Equal(url, candidates[1]);
        }
        finally
        {
            GitHubDownloadProxy.ProxyPrefix = null;
        }
    }

    [Fact]
    public void CandidateUrls_UsesDefaultMirrorChain_WhenNoUserMirror()
    {
        GitHubDownloadProxy.Disabled = false;
        GitHubDownloadProxy.ProxyPrefix = null;
        try
        {
            var url = "https://raw.githubusercontent.com/LibreOffice/dictionaries/master/en/en_US.dic";
            var candidates = GitHubDownloadProxy.CandidateUrls(url).ToList();

            Assert.Equal(GitHubDownloadProxy.DefaultMirrors.Length + 1, candidates.Count);
            Assert.All(candidates.Take(GitHubDownloadProxy.DefaultMirrors.Length),
                c => Assert.StartsWith("https://", c));
            Assert.Equal(url, candidates[^1]);
        }
        finally
        {
            GitHubDownloadProxy.ProxyPrefix = null;
        }
    }

    [Fact]
    public void CandidateUrls_LeavesNonGithubUrlsUntouched()
    {
        var url = "https://huggingface.co/set-soft/audio_separation/resolve/main/Demucs/htdemucs.safetensors";
        var candidates = GitHubDownloadProxy.CandidateUrls(url).ToList();

        Assert.Single(candidates);
        Assert.Equal(url, candidates[0]);
    }

    [Fact]
    public void CandidateUrls_Disabled_ReturnsOnlyOriginal()
    {
        GitHubDownloadProxy.Disabled = true;
        try
        {
            var url = "https://github.com/owner/repo/releases/download/v1.0/pkg.zip";
            var candidates = GitHubDownloadProxy.CandidateUrls(url).ToList();

            Assert.Single(candidates);
            Assert.Equal(url, candidates[0]);
        }
        finally
        {
            GitHubDownloadProxy.Disabled = false;
        }
    }
}
