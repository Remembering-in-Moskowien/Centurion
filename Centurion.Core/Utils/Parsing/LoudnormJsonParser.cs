using System.Globalization;
using Newtonsoft.Json;

namespace Centurion.Core.Utils.Parsing;

/// <summary>loudnorm 滤波器的 JSON 测量输出（两遍法第一遍分析结果）。</summary>
public sealed record LoudnormMeasurements(double InputIntegrated, double InputTruePeak, double InputLra, double InputThreshold, double TargetOffset);

/// <summary>ffmpeg loudnorm 第一遍分析输出的 JSON 实体；字段值均为字符串形式的数值。</summary>
internal sealed class LoudnormOutputJson
{
    /// <summary>输入整体响度（LUFS）。</summary>
    [JsonProperty("input_i")] public string? InputIntegrated { get; set; }

    /// <summary>输入真实峰值（dBTP）。</summary>
    [JsonProperty("input_tp")] public string? InputTruePeak { get; set; }

    /// <summary>输入响度范围（LU）。</summary>
    [JsonProperty("input_lra")] public string? InputLra { get; set; }

    /// <summary>输入响度阈值（LUFS）。</summary>
    [JsonProperty("input_thresh")] public string? InputThreshold { get; set; }

    /// <summary>目标偏移量（LU）。</summary>
    [JsonProperty("target_offset")] public string? TargetOffset { get; set; }
}

/// <summary>
/// 解析 ffmpeg loudnorm 第一遍（analysis）输出的 JSON 测量值
/// </summary>
public static class LoudnormJsonParser
{
    /// <summary>
    /// 从 ffmpeg loudnorm 第一遍分析输出中定位 JSON 片段并解析为测量值。
    /// </summary>
    /// <param name="output">loudnorm 第一遍的标准输出文本。</param>
    /// <returns>解析得到的响度测量值。</returns>
    /// <exception cref="FormatException">未找到 JSON 或缺少所需测量项时抛出。</exception>
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
