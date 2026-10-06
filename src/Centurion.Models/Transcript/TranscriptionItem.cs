using Newtonsoft.Json;

namespace Centurion.Models.Transcript;

// transcription array child item (supports word-level timestamps and confidence)
/// <summary>An entry in the whisper.cpp transcription array (one audio segment).</summary>
public class TranscriptionItem
{
    /// <summary>Start/end timestamps of this segment (as strings, e.g. "0.00").</summary>
    [JsonProperty("timestamps")] public required TimeStampInfo Timestamps { get; set; }

    /// <summary>Start/end offsets of this segment (milliseconds).</summary>
    [JsonProperty("offsets")] public required OffsetInfo Offsets { get; set; }

    /// <summary>Recognized text of this segment.</summary>
    [JsonProperty("text")] public required string Text { get; set; }

    /// <summary>Word-level token details; included only when --output-json-full is used.</summary>
    [JsonProperty("tokens")] public List<TokenInfo>? Tokens { get; set; } // included only when --output-json-full is used
}

// Timestamp info (for segments or words)
/// <summary>Timestamp range as strings.</summary>
public class TimeStampInfo
{
    /// <summary>Start time string.</summary>
    [JsonProperty("from")] public required string From { get; set; }

    /// <summary>End time string.</summary>
    [JsonProperty("to")] public required string To { get; set; }
}

// Offset info (milliseconds, for segments or words)
/// <summary>Integer offset range in milliseconds.</summary>
public class OffsetInfo
{
    /// <summary>Start offset (milliseconds).</summary>
    [JsonProperty("from")] public int From { get; set; }

    /// <summary>End offset (milliseconds).</summary>
    [JsonProperty("to")] public int To { get; set; }
}

// Word-level token info (including confidence)
/// <summary>Recognition details for a word-level token, including time offsets and confidence.</summary>
public class TokenInfo
{
    /// <summary>Text corresponding to this token.</summary>
    [JsonProperty("text")] public required string Text { get; set; }

    /// <summary>Start/end timestamp string of this word.</summary>
    [JsonProperty("timestamps")] public required TimeStampInfo Timestamps { get; set; }

    /// <summary>Start/end offset of this word (milliseconds).</summary>
    [JsonProperty("offsets")] public required OffsetInfo Offsets { get; set; }

    /// <summary>Token ID in the vocabulary.</summary>
    [JsonProperty("id")] public int Id { get; set; }

    /// <summary>Recognition confidence (probability).</summary>
    [JsonProperty("p")] public float P { get; set; } // confidence (probability)

    /// <summary>Time offset related to DTW forced alignment.</summary>
    [JsonProperty("t_dtw")] public int Tdtw { get; set; }
}
