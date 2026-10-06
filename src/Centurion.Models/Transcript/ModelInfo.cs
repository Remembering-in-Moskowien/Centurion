using Newtonsoft.Json;

namespace Centurion.Models.Transcript;

// model node
/// <summary>Model architecture info from the model node in whisper.cpp output JSON.</summary>
public class ModelInfo
{
    /// <summary>Model type name (e.g. "whisper").</summary>
    [JsonProperty("type")] public required string Type { get; set; }

    /// <summary>Whether the model supports multiple languages.</summary>
    [JsonProperty("multilingual")] public bool Multilingual { get; set; }

    /// <summary>Vocabulary size.</summary>
    [JsonProperty("vocab")] public int Vocab { get; set; }

    /// <summary>Audio encoder architecture info.</summary>
    [JsonProperty("audio")] public required AudioInfo Audio { get; set; }

    /// <summary>Text decoder architecture info.</summary>
    [JsonProperty("text")] public required TextInfo Text { get; set; }

    /// <summary>Number of Mel bands.</summary>
    [JsonProperty("mels")] public int Mels { get; set; }

    /// <summary>Model file type (quantization precision identifier).</summary>
    [JsonProperty("ftype")] public int Ftype { get; set; }
}

/// <summary>Transformer hyperparameters of the audio encoder.</summary>
public class AudioInfo
{
    /// <summary>Context length.</summary>
    [JsonProperty("ctx")] public int Ctx { get; set; }

    /// <summary>State dimension.</summary>
    [JsonProperty("state")] public int State { get; set; }

    /// <summary>Number of attention heads.</summary>
    [JsonProperty("head")] public int Head { get; set; }

    /// <summary>Number of encoder layers.</summary>
    [JsonProperty("layer")] public int Layer { get; set; }
}

/// <summary>Transformer hyperparameters of the text decoder.</summary>
public class TextInfo
{
    /// <summary>Context length.</summary>
    [JsonProperty("ctx")] public int Ctx { get; set; }

    /// <summary>State dimension.</summary>
    [JsonProperty("state")] public int State { get; set; }

    /// <summary>Number of attention heads.</summary>
    [JsonProperty("head")] public int Head { get; set; }

    /// <summary>Number of decoder layers.</summary>
    [JsonProperty("layer")] public int Layer { get; set; }
}
