using Newtonsoft.Json;

namespace Centurion.Models.Transcript;

/// <summary>CrispASR -ojf 输出的完整转录 JSON 根对象，whisper / qwen3 等后端通用。</summary>
public class CrispAsrTranscriptJson
{
    /// <summary>CrispASR 头部元信息（后端、模型、语言）。</summary>
    [JsonProperty("crispasr")] public required CrispAsrHeader Header { get; set; }

    /// <summary>逐段转写条目列表。</summary>
    [JsonProperty("transcription")] public required List<CrispAsrTranscriptionItem> Transcription { get; set; }
}

/// <summary>CrispASR 输出根对象中的头部元信息。</summary>
public class CrispAsrHeader
{
    /// <summary>转录后端名称（如 whisper、qwen3）。</summary>
    [JsonProperty("backend")] public required string Backend { get; set; }

    /// <summary>所用模型的文件路径。</summary>
    [JsonProperty("model")] public required string Model { get; set; }

    /// <summary>音频语言代码；"auto" 表示由模型自动检测。</summary>
    [JsonProperty("language")] public required string Language { get; set; }
}

/// <summary>CrispASR 输出中 transcription 数组的一个条目（一个音频段）。</summary>
public class CrispAsrTranscriptionItem
{
    /// <summary>该段的起止时间戳（字符串形式，如 "00:00:01,260"）。</summary>
    [JsonProperty("timestamps")] public required TimeStampInfo Timestamps { get; set; }

    /// <summary>该段的起止偏移（毫秒）。</summary>
    [JsonProperty("offsets")] public required OffsetInfo Offsets { get; set; }

    /// <summary>该段识别文本。</summary>
    [JsonProperty("text")] public required string Text { get; set; }

    /// <summary>说话人标签（如 "(speaker 0) "）；仅 --diarize 输出包含，纯转录时为 null。</summary>
    [JsonProperty("speaker")] public string? Speaker { get; set; }

    /// <summary>音频块序号（长音频按块处理时的块编号）。</summary>
    [JsonProperty("chunk_id")] public int ChunkId { get; set; }

    /// <summary>词级时间戳明细；部分后端（如 qwen3）可能不输出该字段。</summary>
    [JsonProperty("words")] public List<CrispAsrWordInfo>? Words { get; set; }

    /// <summary>词级 Token 明细（含置信度），部分后端可能不输出该字段。</summary>
    [JsonProperty("tokens")] public List<CrispAsrTokenInfo>? Tokens { get; set; }
}

/// <summary>CrispASR 词级时间戳信息；t0/t1 单位为厘秒（毫秒 / 10）。</summary>
public class CrispAsrWordInfo
{
    /// <summary>词文本（可能带前导空格）。</summary>
    [JsonProperty("text")] public required string Text { get; set; }

    /// <summary>起始时间（厘秒）。</summary>
    [JsonProperty("t0")] public int T0 { get; set; }

    /// <summary>结束时间（厘秒）。</summary>
    [JsonProperty("t1")] public int T1 { get; set; }

    /// <summary>毫秒级起止偏移。</summary>
    [JsonProperty("offsets")] public required OffsetInfo Offsets { get; set; }
}

/// <summary>CrispASR 词级 Token 信息（含置信度）；无时间信息时 t0/t1 为 -1。</summary>
public class CrispAsrTokenInfo
{
    /// <summary>Token 文本（可能带前导空格）。</summary>
    [JsonProperty("text")] public required string Text { get; set; }

    /// <summary>识别置信度（概率，0~1）。</summary>
    [JsonProperty("p")] public double P { get; set; }

    /// <summary>起始时间（厘秒），无时间信息时为 -1。</summary>
    [JsonProperty("t0")] public int T0 { get; set; }

    /// <summary>结束时间（厘秒），无时间信息时为 -1。</summary>
    [JsonProperty("t1")] public int T1 { get; set; }

    /// <summary>毫秒级起止偏移。</summary>
    [JsonProperty("offsets")] public required OffsetInfo Offsets { get; set; }
}
