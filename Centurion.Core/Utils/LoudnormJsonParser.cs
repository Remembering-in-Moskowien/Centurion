using System.Globalization;
using Newtonsoft.Json.Linq;

namespace Centurion.Core.Utils;

/// <summary>
/// loudnorm 滤波器的 JSON 测量输出（两遍法第一遍分析结果）
/// </summary>
public sealed record LoudnormMeasurements(double InputIntegrated, double InputTruePeak, double InputLra, double InputThreshold, double TargetOffset);

/// <summary>
/// 解析 ffmpeg loudnorm 第一遍（analysis）输出的 JSON 测量值
/// </summary>
public static class LoudnormJsonParser
{
    public static LoudnormMeasurements Parse(string output)
    {
        var start = output.LastIndexOf("{", StringComparison.Ordinal);
        var end = output.LastIndexOf("}", StringComparison.Ordinal);
        if (start < 0 || end <= start)
            throw new FormatException("loudnorm JSON was not found.");
        var root = JsonParser.Deserialize<JObject>(output[start..(end + 1)]);
        return new LoudnormMeasurements(Read(root, "input_i"), Read(root, "input_tp"), Read(root, "input_lra"), Read(root, "input_thresh"), Read(root, "target_offset"));
    }

    private static double Read(JObject root, string name) => double.Parse(root[name]?.Value<string>() ?? throw new FormatException($"Missing loudnorm value '{name}'."), CultureInfo.InvariantCulture);
}
