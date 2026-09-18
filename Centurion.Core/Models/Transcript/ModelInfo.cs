using Newtonsoft.Json;

namespace Centurion.Core.Models.Transcript;

// model 节点
public class ModelInfo
{
    [JsonProperty("type")] public required string Type { get; set; }

    [JsonProperty("multilingual")] public bool Multilingual { get; set; }

    [JsonProperty("vocab")] public int Vocab { get; set; }

    [JsonProperty("audio")] public required AudioInfo Audio { get; set; }

    [JsonProperty("text")] public required TextInfo Text { get; set; }

    [JsonProperty("mels")] public int Mels { get; set; }

    [JsonProperty("ftype")] public int Ftype { get; set; }
}

public class AudioInfo
{
    [JsonProperty("ctx")] public int Ctx { get; set; }

    [JsonProperty("state")] public int State { get; set; }

    [JsonProperty("head")] public int Head { get; set; }

    [JsonProperty("layer")] public int Layer { get; set; }
}

public class TextInfo
{
    [JsonProperty("ctx")] public int Ctx { get; set; }

    [JsonProperty("state")] public int State { get; set; }

    [JsonProperty("head")] public int Head { get; set; }

    [JsonProperty("layer")] public int Layer { get; set; }
}
