using Newtonsoft.Json;

namespace Centurion.Models.Transcript;

// 根对象
/// <summary>whisper.cpp --output-json-full 产出的完整转写 JSON 根对象。</summary>
public class WhisperTranscriptJson
{
    /// <summary>系统/构建信息字符串。</summary>
    [JsonProperty("systeminfo")] public required string SystemInfo { get; set; }

    /// <summary>所用模型的结构信息。</summary>
    [JsonProperty("model")] public required ModelInfo Model { get; set; }

    /// <summary>本次推理使用的参数。</summary>
    [JsonProperty("params")] public required ParamsInfo Params { get; set; }

    /// <summary>识别结果摘要（含检测语言）。</summary>
    [JsonProperty("result")] public required ResultInfo Result { get; set; }

    /// <summary>逐段转写条目列表。</summary>
    [JsonProperty("transcription")] public required List<TranscriptionItem> Transcription { get; set; }
}
