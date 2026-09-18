// Centurion.Core/Utils/DiarizationJsonParser.cs

using Centurion.Abstractions.Strategy;
using Newtonsoft.Json.Linq;

namespace Centurion.Core.Utils;

/// <summary>
/// 解析 CrispASR 的 diarized JSON 输出（-ojf 格式），提取说话人时间片段。
/// 输入结构：{"segments": [{"start": 0.0, "end": 26.1, "speaker": "A", ...}, ...]}（时间单位：秒）。
/// </summary>
public static class DiarizationJsonParser
{
    public static IReadOnlyList<SpeakerSegment> Parse(string json)
    {
        var root = JsonParser.Deserialize<JObject>(json);

        if (root["segments"] is not JArray segments)
            throw new InvalidOperationException("Missing 'segments' array in diarized JSON output.");

        var result = new List<SpeakerSegment>();
        foreach (var segment in segments.OfType<JObject>())
        {
            var start = segment["start"]?.Value<double?>();
            var end = segment["end"]?.Value<double?>();
            var speaker = segment["speaker"]?.Value<string>();
            if (start is null || end is null || string.IsNullOrWhiteSpace(speaker))
                continue;

            result.Add(new SpeakerSegment(start.Value, end.Value, speaker));
        }

        return result;
    }
}
