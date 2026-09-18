using Newtonsoft.Json;

namespace Centurion.Core.Models.Transcript;

// result 节点
public class ResultInfo
{
    [JsonProperty("language")] public required string Language { get; set; }
}
