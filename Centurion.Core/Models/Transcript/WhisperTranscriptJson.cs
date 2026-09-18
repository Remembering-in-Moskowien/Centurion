using Newtonsoft.Json;

namespace Centurion.Core.Models.Transcript;

// 根对象
public class WhisperTranscriptJson
{
    [JsonProperty("systeminfo")] public required string SystemInfo { get; set; }

    [JsonProperty("model")] public required ModelInfo Model { get; set; }

    [JsonProperty("params")] public required ParamsInfo Params { get; set; }

    [JsonProperty("result")] public required ResultInfo Result { get; set; }

    [JsonProperty("transcription")] public required List<TranscriptionItem> Transcription { get; set; }
}
