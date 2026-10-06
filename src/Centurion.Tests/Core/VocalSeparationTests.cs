using Centurion.Core.Workflow.Strategy.VocalSeparation;using Xunit;

namespace Centurion.Tests.Core;

public sealed class VocalSeparationTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(257985, 1)] // exactly one stride
    [InlineData(343980, 2)] // one full segment: an extra padded chunk covers the overlap
    [InlineData(343981, 2)]
    [InlineData(600000, 3)]
    [InlineData(687960, 3)] // exactly 2 full segments with overlap
    [InlineData(0)]
    public void ComputeChunkCount_CoversInputWithOverlap(int totalSamples, int? expected = null)
    {
        if (expected is null)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => HtDemucsOnnxEngine.ComputeChunkCount(totalSamples));
            return;
        }
        Assert.Equal(expected.Value, HtDemucsOnnxEngine.ComputeChunkCount(totalSamples));
    }

    [Fact]
    public void BuildTransitionWindow_FadesFirstAndLastQuarter()
    {
        var window = HtDemucsOnnxEngine.BuildTransitionWindow();
        Assert.Equal(HtDemucsOnnxEngine.SegmentSamples, window.Length);

        var fade = HtDemucsOnnxEngine.Fade;
        Assert.Equal(0f, window[0], precision: 4);
        Assert.Equal(1f, window[fade], precision: 4);
        Assert.Equal(1f, window[HtDemucsOnnxEngine.SegmentSamples - 1 - fade], precision: 4);
        Assert.Equal(0f, window[^1], precision: 4);
        // Symmetric.
        Assert.Equal(window[fade / 2], window[HtDemucsOnnxEngine.SegmentSamples - 1 - fade / 2], precision: 4);
    }

    [Theory]
    [InlineData("htdemucs", "htdemucs_fp16weights.onnx", "StemSplitio/htdemucs-onnx", "drums,bass,other,vocals")]
    [InlineData("HTDEMUCS", "htdemucs_fp16weights.onnx", "StemSplitio/htdemucs-onnx", "drums,bass,other,vocals")]
    [InlineData("htdemucs_6s", "htdemucs_6s_fp16weights.onnx", "StemSplitio/htdemucs-6s-onnx", "drums,bass,other,vocals,guitar,piano")]
    [InlineData("htdemucs-6s", "htdemucs_6s_fp16weights.onnx", "StemSplitio/htdemucs-6s-onnx", "drums,bass,other,vocals,guitar,piano")]
    [InlineData("htdemucs-ft", "htdemucs_fp16weights.onnx", "StemSplitio/htdemucs-onnx", "drums,bass,other,vocals")]
    [InlineData("unknown-model", null, null, null)]
    public void GetOnnxModelInfo_MapsKnownModels(
        string model, string? fileName, string? repo, string? sourcesCsv)
    {
        var repo2 = HtDemucsOnnxEngine.GetOnnxModelInfo(model, out var fn, out var sources);
        Assert.Equal(repo, repo2);
        if (fileName is not null)
        {
            Assert.Equal(fileName, fn);
            Assert.Equal(sourcesCsv, string.Join(',', sources));
            Assert.Contains("vocals", sources);
            Assert.Equal(3, sources.ToList().IndexOf("vocals"));
        }
    }

    [Fact]
    public void BuildModelUrl_UsesHuggingFaceResolve()
    {
        Assert.Equal(
            "https://huggingface.co/StemSplitio/htdemucs-onnx/resolve/main/htdemucs_fp16weights.onnx",
            HtDemucsOnnxEngine.BuildModelUrl("StemSplitio/htdemucs-onnx", "htdemucs_fp16weights.onnx"));
    }

    [Fact]
    public void VocalsIndex_PointsAtFourthSource()
    {
        Assert.Equal(3, HtDemucsOnnxEngine.VocalsIndex);
        Assert.Equal("vocals", HtDemucsOnnxEngine.Sources[HtDemucsOnnxEngine.VocalsIndex]);
        Assert.Equal("vocals", HtDemucsOnnxEngine.Sources6[HtDemucsOnnxEngine.VocalsIndex]);
    }
}
