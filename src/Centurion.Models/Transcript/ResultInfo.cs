using Newtonsoft.Json;

namespace Centurion.Models.Transcript;

// result node
/// <summary>Recognition result summary from the result node in whisper.cpp output JSON.</summary>
public class ResultInfo
{
    /// <summary>Actually detected language code.</summary>
    [JsonProperty("language")] public required string Language { get; set; }
}
