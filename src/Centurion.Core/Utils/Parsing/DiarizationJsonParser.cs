using Centurion.Abstractions.Strategy;
using Centurion.Models.Transcript;

namespace Centurion.Core.Utils.Parsing;

/// <summary>
/// 解析 CrispASR --diarize-speakers 输出的 JSON（-ojf 格式），提取说话人时间片段。
/// 说话人信息位于 transcription 数组每个条目的 speaker 字段（如 "(speaker 0)"），时间取自该条目的毫秒偏移。
/// </summary>
public static class DiarizationJsonParser
{
    /// <summary>
    /// 解析 CrispASR --diarize-speakers 输出，提取各说话人时间片段。
    /// 缺少 speaker 标签的条目会被忽略。
    /// </summary>
    /// <param name="json">CrispASR -ojf 格式的 JSON 字符串。</param>
    /// <returns>解析得到的说话人片段列表（时间为秒）。</returns>
    /// <exception cref="InvalidOperationException">输出中缺少 transcription 数组时抛出。</exception>
    public static IReadOnlyList<SpeakerSegment> Parse(string json)
    {
        var root = JsonParser.Deserialize<CrispAsrTranscriptJson>(json);

        if (root.Transcription is null)
            throw new InvalidOperationException("Missing 'transcription' array in diarized JSON output.");

        var result = new List<SpeakerSegment>();
        foreach (var item in root.Transcription)
        {
            var speaker = ExtractSpeaker(item.Speaker);
            if (speaker is null)
                continue;

            result.Add(new SpeakerSegment(
                item.Offsets.From / 1000.0,
                item.Offsets.To / 1000.0,
                speaker));
        }

        return result;
    }

    /// <summary>
    /// 规范化说话人标签："(speaker 0) " → "speaker 0"；非括号格式保留原文并去除首尾空白。
    /// </summary>
    /// <param name="raw">CrispASR 输出的原始 speaker 字段值。</param>
    /// <returns>规范化后的标签；空白或空值时返回 null。</returns>
    private static string? ExtractSpeaker(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var trimmed = raw.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '(' && trimmed[^1] == ')')
            return trimmed[1..^1].Trim();

        return trimmed;
    }
}
