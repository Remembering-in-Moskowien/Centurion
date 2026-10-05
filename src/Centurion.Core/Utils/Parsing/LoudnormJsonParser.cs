using System.Globalization;
using Newtonsoft.Json;

namespace Centurion.Core.Utils.Parsing;

/// <summary>The JSON measurement output of the loudnorm filter (the first-pass analysis result of the two-pass method).</summary>
public sealed record LoudnormMeasurements(double InputIntegrated, double InputTruePeak, double InputLra, double InputThreshold, double TargetOffset);

/// <summary>The JSON entity of the ffmpeg loudnorm first-pass analysis output; all field values are numbers in string form.</summary>
internal sealed class LoudnormOutputJson
{
    /// <summary>Input integrated loudness (LUFS).</summary>
    [JsonProperty("input_i")] public string? InputIntegrated { get; set; }

    /// <summary>Input true peak (dBTP).</summary>
    [JsonProperty("input_tp")] public string? InputTruePeak { get; set; }

    /// <summary>Input loudness range (LU).</summary>
    [JsonProperty("input_lra")] public string? InputLra { get; set; }

    /// <summary>Input loudness threshold (LUFS).</summary>
    [JsonProperty("input_thresh")] public string? InputThreshold { get; set; }

    /// <summary>Target offset (LU).</summary>
    [JsonProperty("target_offset")] public string? TargetOffset { get; set; }
}

/// <summary>
/// Parse the JSON measurement values from the ffmpeg loudnorm first-pass (analysis) output.
/// </summary>
public static class LoudnormJsonParser
{
    /// <summary>
    /// Locate the JSON fragment in the ffmpeg loudnorm first-pass analysis output and parse it into measurements.
    /// </summary>
    /// <param name="output">The standard output text of the loudnorm first pass.</param>
    /// <returns>The parsed loudness measurements.</returns>
    /// <exception cref="FormatException">Thrown when no JSON is found or a required measurement is missing.</exception>
    public static LoudnormMeasurements Parse(string output)
    {
        var start = output.LastIndexOf("{", StringComparison.Ordinal);
        var end = output.LastIndexOf("}", StringComparison.Ordinal);
        if (start < 0 || end <= start)
            throw new FormatException("loudnorm JSON was not found.");

        var dto = JsonParser.Deserialize<LoudnormOutputJson>(output[start..(end + 1)]);
        return new LoudnormMeasurements(
            Read(dto.InputIntegrated, "input_i"),
            Read(dto.InputTruePeak, "input_tp"),
            Read(dto.InputLra, "input_lra"),
            Read(dto.InputThreshold, "input_thresh"),
            Read(dto.TargetOffset, "target_offset"));
    }

    private static double Read(string? value, string name) =>
        double.Parse(value ?? throw new FormatException($"Missing loudnorm value '{name}'."), CultureInfo.InvariantCulture);
}
