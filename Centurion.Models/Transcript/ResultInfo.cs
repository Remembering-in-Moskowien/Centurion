using Newtonsoft.Json;

namespace Centurion.Models.Transcript;

// result 节点
public class ResultInfo
{
    [JsonProperty("language")] public required string Language { get; set; }
}
