using Newtonsoft.Json;

namespace Centurion.Models.Transcript;

// Root object
/// <summary>Full transcript JSON root object produced by whisper.cpp --output-json-full.</summary>
public class WhisperTranscriptJson
{
    /// <summary>System/build info string.</summary>
    [JsonProperty("systeminfo")] public required string SystemInfo { get; set; }

    /// <summary>Architecture info of the model used.</summary>
    [JsonProperty("model")] public required ModelInfo Model { get; set; }

    /// <summary>Parameters used for this inference run.</summary>
    [JsonProperty("params")] public required ParamsInfo Params { get; set; }

    /// <summary>Recognition result summary (including detected language).</summary>
    [JsonProperty("result")] public required ResultInfo Result { get; set; }

    /// <summary>List of per-segment transcription entries.</summary>
    [JsonProperty("transcription")] public required List<TranscriptionItem> Transcription { get; set; }
}
