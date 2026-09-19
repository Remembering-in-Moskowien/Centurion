using Newtonsoft.Json;

namespace Centurion.Models.Transcript;

// model 节点
/// <summary>whisper.cpp 输出 JSON 中 model 节点的模型结构信息。</summary>
public class ModelInfo
{
    /// <summary>模型类型名（如 "whisper"）。</summary>
    [JsonProperty("type")] public required string Type { get; set; }

    /// <summary>模型是否支持多语言。</summary>
    [JsonProperty("multilingual")] public bool Multilingual { get; set; }

    /// <summary>词表大小。</summary>
    [JsonProperty("vocab")] public int Vocab { get; set; }

    /// <summary>音频编码器结构信息。</summary>
    [JsonProperty("audio")] public required AudioInfo Audio { get; set; }

    /// <summary>文本解码器结构信息。</summary>
    [JsonProperty("text")] public required TextInfo Text { get; set; }

    /// <summary>Mel 频带数量。</summary>
    [JsonProperty("mels")] public int Mels { get; set; }

    /// <summary>模型文件类型（量化精度标识）。</summary>
    [JsonProperty("ftype")] public int Ftype { get; set; }
}

/// <summary>音频编码器的 Transformer 结构超参。</summary>
public class AudioInfo
{
    /// <summary>上下文长度。</summary>
    [JsonProperty("ctx")] public int Ctx { get; set; }

    /// <summary>状态维度。</summary>
    [JsonProperty("state")] public int State { get; set; }

    /// <summary>注意力头数。</summary>
    [JsonProperty("head")] public int Head { get; set; }

    /// <summary>编码器层数。</summary>
    [JsonProperty("layer")] public int Layer { get; set; }
}

/// <summary>文本解码器的 Transformer 结构超参。</summary>
public class TextInfo
{
    /// <summary>上下文长度。</summary>
    [JsonProperty("ctx")] public int Ctx { get; set; }

    /// <summary>状态维度。</summary>
    [JsonProperty("state")] public int State { get; set; }

    /// <summary>注意力头数。</summary>
    [JsonProperty("head")] public int Head { get; set; }

    /// <summary>解码器层数。</summary>
    [JsonProperty("layer")] public int Layer { get; set; }
}
