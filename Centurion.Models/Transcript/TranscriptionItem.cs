using Newtonsoft.Json;

namespace Centurion.Models.Transcript;

// transcription 数组子项（支持词级时间戳和置信度）
public class TranscriptionItem
{
    [JsonProperty("timestamps")] public required TimeStampInfo Timestamps { get; set; }

    [JsonProperty("offsets")] public required OffsetInfo Offsets { get; set; }

    [JsonProperty("text")] public required string Text { get; set; }

    [JsonProperty("tokens")] public List<TokenInfo>? Tokens { get; set; } // 当使用 --output-json-full 时包含此项
}

// 时间戳信息（用于段或词）
public class TimeStampInfo
{
    [JsonProperty("from")] public required string From { get; set; }

    [JsonProperty("to")] public required string To { get; set; }
}

// 偏移信息（毫秒，用于段或词）
public class OffsetInfo
{
    [JsonProperty("from")] public int From { get; set; }

    [JsonProperty("to")] public int To { get; set; }
}

// 词级 Token 信息（包含置信度）
public class TokenInfo
{
    [JsonProperty("text")] public required string Text { get; set; }

    [JsonProperty("timestamps")] public required TimeStampInfo Timestamps { get; set; }

    [JsonProperty("offsets")] public required OffsetInfo Offsets { get; set; }

    [JsonProperty("id")] public int Id { get; set; }

    [JsonProperty("p")] public float P { get; set; } // 置信度（概率）

    [JsonProperty("t_dtw")] public int Tdtw { get; set; }
}
