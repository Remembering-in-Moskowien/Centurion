using Newtonsoft.Json;

namespace Centurion.Models.Transcript;

/// <summary>Full transcript JSON root object output by CrispASR -ojf; shared by whisper / qwen3 and other backends.</summary>
public class CrispAsrTranscriptJson
{
    /// <summary>CrispASR header metadata (backend, model, language).</summary>
    [JsonProperty("crispasr")] public required CrispAsrHeader Header { get; set; }

    /// <summary>List of per-segment transcription entries.</summary>
    [JsonProperty("transcription")] public required List<CrispAsrTranscriptionItem> Transcription { get; set; }
}

/// <summary>Header metadata in the CrispASR output root object.</summary>
public class CrispAsrHeader
{
    /// <summary>Transcription backend name (e.g. whisper, qwen3).</summary>
    [JsonProperty("backend")] public required string Backend { get; set; }

    /// <summary>File path of the model used.</summary>
    [JsonProperty("model")] public required string Model { get; set; }

    /// <summary>Audio language code; "auto" means the model detects it automatically.</summary>
    [JsonProperty("language")] public required string Language { get; set; }
}

/// <summary>An entry in the CrispASR transcription array (one audio segment).</summary>
public class CrispAsrTranscriptionItem
{
    /// <summary>Start/end timestamps of this segment (as strings, e.g. "00:00:01,260").</summary>
    [JsonProperty("timestamps")] public required TimeStampInfo Timestamps { get; set; }

    /// <summary>Start/end offsets of this segment (milliseconds).</summary>
    [JsonProperty("offsets")] public required OffsetInfo Offsets { get; set; }

    /// <summary>Recognized text of this segment.</summary>
    [JsonProperty("text")] public required string Text { get; set; }

    /// <summary>Speaker tag (e.g. "(speaker 0) "); present only in --diarize output, null for plain transcription.</summary>
    [JsonProperty("speaker")] public string? Speaker { get; set; }

    /// <summary>Audio chunk index (chunk number when long audio is processed in chunks).</summary>
    [JsonProperty("chunk_id")] public int ChunkId { get; set; }

    /// <summary>Word-level timestamp details; some backends (e.g. qwen3) may omit this field.</summary>
    [JsonProperty("words")] public List<CrispAsrWordInfo>? Words { get; set; }

    /// <summary>Word-level token details (including confidence); some backends may omit this field.</summary>
    [JsonProperty("tokens")] public List<CrispAsrTokenInfo>? Tokens { get; set; }
}

/// <summary>CrispASR word-level timestamp info; t0/t1 are in centiseconds (milliseconds / 10).</summary>
public class CrispAsrWordInfo
{
    /// <summary>Word text (may include a leading space).</summary>
    [JsonProperty("text")] public required string Text { get; set; }

    /// <summary>Start time (centiseconds).</summary>
    [JsonProperty("t0")] public int T0 { get; set; }

    /// <summary>End time (centiseconds).</summary>
    [JsonProperty("t1")] public int T1 { get; set; }

    /// <summary>Start/end offsets in milliseconds.</summary>
    [JsonProperty("offsets")] public required OffsetInfo Offsets { get; set; }
}

/// <summary>CrispASR word-level token info (including confidence); t0/t1 are -1 when no timing info is available.</summary>
public class CrispAsrTokenInfo
{
    /// <summary>Token text (may include a leading space).</summary>
    [JsonProperty("text")] public required string Text { get; set; }

    /// <summary>Recognition confidence (probability, 0 to 1).</summary>
    [JsonProperty("p")] public double P { get; set; }

    /// <summary>Start time (centiseconds); -1 when no timing info is available.</summary>
    [JsonProperty("t0")] public int T0 { get; set; }

    /// <summary>End time (centiseconds); -1 when no timing info is available.</summary>
    [JsonProperty("t1")] public int T1 { get; set; }

    /// <summary>Start/end offsets in milliseconds.</summary>
    [JsonProperty("offsets")] public required OffsetInfo Offsets { get; set; }
}
