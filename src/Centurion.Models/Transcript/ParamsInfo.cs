using Newtonsoft.Json;

namespace Centurion.Models.Transcript;

// params node
/// <summary>Inference parameters of this run from the params node in whisper.cpp output JSON.</summary>
public class ParamsInfo
{
    /// <summary>Name of the model used.</summary>
    [JsonProperty("model")] public required string Model { get; set; }

    /// <summary>Recognition language code (e.g. "zh", "en").</summary>
    [JsonProperty("language")] public required string Language { get; set; }

    /// <summary>Whether a translation task was executed (translated to English).</summary>
    [JsonProperty("translate")] public bool Translate { get; set; }
}
