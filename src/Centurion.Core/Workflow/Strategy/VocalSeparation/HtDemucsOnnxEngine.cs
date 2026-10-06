using System;
using System.IO;
using System.Collections.Generic;

namespace Centurion.Core.Workflow.Strategy.VocalSeparation;

/// <summary>
/// DSP constants and helpers for htdemucs ONNX inference (MIT-licensed StemSplitio exports).
/// The exported graphs bind to a fixed (1, 2, 343980) segment — 7.8 s at 44.1 kHz stereo —
/// with no dynamic axes; longer audio is processed with quarter-segment overlap-add using a
/// triangular transition window (same scheme as demucs/demucs-onnx).
/// </summary>
public static class HtDemucsOnnxEngine
{
    /// <summary>The htdemucs model's hard-coded sample rate.</summary>
    public const int SampleRate = 44100;

    /// <summary>Fixed segment length baked into the exported graphs (7.8 s).</summary>
    public const int SegmentSamples = 343980;

    /// <summary>Model channel layout (stereo).</summary>
    public const int ChannelCount = 2;

    /// <summary>Maximum stem count across supported single-file models (htdemucs / htdemucs_6s).</summary>
    public const int MaxSources = 6;

    /// <summary>Stem index of the vocals track in the output tensor (drums, bass, other, vocals[, guitar, piano]).</summary>
    public const int VocalsIndex = 3;

    /// <summary>Stem order of the htdemucs model output.</summary>
    public static readonly IReadOnlyList<string> Sources = ["drums", "bass", "other", "vocals"];

    /// <summary>Stem order of the htdemucs_6s model output.</summary>
    public static readonly IReadOnlyList<string> Sources6 = ["drums", "bass", "other", "vocals", "guitar", "piano"];

    /// <summary>Overlap-add stride: quarter-segment overlap.</summary>
    public static int Overlap => SegmentSamples / 4;

    /// <summary>Window fade length: the first/last quarter of a segment.</summary>
    public static int Fade => Overlap;

    /// <summary>
    /// Number of fixed-size chunks needed to cover <paramref name="totalSamples"/> with
    /// quarter-segment overlap (at least 1).
    /// </summary>
    internal static int ComputeChunkCount(int totalSamples)
    {
        if (totalSamples <= 0)
            throw new ArgumentOutOfRangeException(nameof(totalSamples));
        var stride = SegmentSamples - Overlap;
        return Math.Max(1, (totalSamples + stride - 1) / stride);
    }

    /// <summary>
    /// Builds the triangular transition window used for overlap-add: ones in the middle,
    /// linear fade-in over the first quarter and fade-out over the last quarter.
    /// </summary>
    internal static float[] BuildTransitionWindow()
    {
        var window = new float[SegmentSamples];
        for (var i = 0; i < SegmentSamples; i++)
            window[i] = 1f;
        for (var i = 0; i < Fade; i++)
        {
            var t = (float)i / Fade;
            window[i] = t;
            window[SegmentSamples - 1 - i] = t;
        }
        return window;
    }

    /// <summary>ONNX model information for a configured model name.</summary>
    /// <param name="model">Model name, e.g. "htdemucs" or "htdemucs_6s".</param>
    /// <param name="fileName">Output: the ONNX file name to download.</param>
    /// <param name="sources">Output: stem order of the model's output tensor.</param>
    /// <returns>The HuggingFace repo id, or null for unknown models.</returns>
    internal static string? GetOnnxModelInfo(string model, out string fileName, out IReadOnlyList<string> sources)
    {
        fileName = "";
        sources = Sources;
        switch (model.Trim().ToLowerInvariant())
        {
            case "htdemucs":
                fileName = "htdemucs_fp16weights.onnx";
                sources = Sources;
                return "StemSplitio/htdemucs-onnx";
            case "htdemucs_6s" or "htdemucs-6s" or "htdemucs6s":
                fileName = "htdemucs_6s_fp16weights.onnx";
                sources = Sources6;
                return "StemSplitio/htdemucs-6s-onnx";
            case "htdemucs_ft" or "htdemucs-ft":
                // No single-file ONNX for the FT bag: fall back to the single-file htdemucs.
                fileName = "htdemucs_fp16weights.onnx";
                sources = Sources;
                return "StemSplitio/htdemucs-onnx";
            default:
                return null;
        }
    }

    /// <summary>Builds the HuggingFace resolve URL for a repo file.</summary>
    internal static string BuildModelUrl(string repoId, string fileName) =>
        $"https://huggingface.co/{repoId}/resolve/main/{fileName}";
}
