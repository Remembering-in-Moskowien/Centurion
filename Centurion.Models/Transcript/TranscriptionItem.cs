using Newtonsoft.Json;

namespace Centurion.Models.Transcript;

// transcription 数组子项（支持词级时间戳和置信度）
/// <summary>whisper.cpp 输出 JSON 中 transcription 数组的一个条目（一个音频段）。</summary>
public class TranscriptionItem
{
    /// <summary>该段的起止时间戳（字符串形式，如 "0.00"）。</summary>
    [JsonProperty("timestamps")] public required TimeStampInfo Timestamps { get; set; }

    /// <summary>该段的起止偏移（毫秒）。</summary>
    [JsonProperty("offsets")] public required OffsetInfo Offsets { get; set; }

    /// <summary>该段识别文本。</summary>
    [JsonProperty("text")] public required string Text { get; set; }

    /// <summary>词级 Token 明细，仅当使用 --output-json-full 时包含。</summary>
    [JsonProperty("tokens")] public List<TokenInfo>? Tokens { get; set; } // 当使用 --output-json-full 时包含此项
}

// 时间戳信息（用于段或词）
/// <summary>字符串形式的时间戳区间。</summary>
public class TimeStampInfo
{
    /// <summary>起始时间字符串。</summary>
    [JsonProperty("from")] public required string From { get; set; }

    /// <summary>结束时间字符串。</summary>
    [JsonProperty("to")] public required string To { get; set; }
}

// 偏移信息（毫秒，用于段或词）
/// <summary>毫秒级整数偏移区间。</summary>
public class OffsetInfo
{
    /// <summary>起始偏移（毫秒）。</summary>
    [JsonProperty("from")] public int From { get; set; }

    /// <summary>结束偏移（毫秒）。</summary>
    [JsonProperty("to")] public int To { get; set; }
}

// 词级 Token 信息（包含置信度）
/// <summary>词级 Token 的识别明细，含时间偏移、置信度等。</summary>
public class TokenInfo
{
    /// <summary>该 Token 对应的文本。</summary>
    [JsonProperty("text")] public required string Text { get; set; }

    /// <summary>该词的起止时间戳字符串。</summary>
    [JsonProperty("timestamps")] public required TimeStampInfo Timestamps { get; set; }

    /// <summary>该词的起止偏移（毫秒）。</summary>
    [JsonProperty("offsets")] public required OffsetInfo Offsets { get; set; }

    /// <summary>词表中的 Token 编号。</summary>
    [JsonProperty("id")] public int Id { get; set; }

    /// <summary>识别置信度（概率）。</summary>
    [JsonProperty("p")] public float P { get; set; } // 置信度（概率）

    /// <summary>DTW 强制对齐相关的时间偏移值。</summary>
    [JsonProperty("t_dtw")] public int Tdtw { get; set; }
}
