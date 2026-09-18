using Newtonsoft.Json;

namespace Centurion.Core.Models.Transcript;

// params 节点
public class ParamsInfo
{
    [JsonProperty("model")] public required string Model { get; set; }

    [JsonProperty("language")] public required string Language { get; set; }

    [JsonProperty("translate")] public bool Translate { get; set; }
}
