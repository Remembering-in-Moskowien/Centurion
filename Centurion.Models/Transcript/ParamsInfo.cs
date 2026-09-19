using Newtonsoft.Json;

namespace Centurion.Models.Transcript;

// params 节点
/// <summary>whisper.cpp 输出 JSON 中 params 节点的本次推理参数。</summary>
public class ParamsInfo
{
    /// <summary>使用的模型名称。</summary>
    [JsonProperty("model")] public required string Model { get; set; }

    /// <summary>识别语言代码（如 "zh"、"en"）。</summary>
    [JsonProperty("language")] public required string Language { get; set; }

    /// <summary>是否执行了翻译任务（译为英文）。</summary>
    [JsonProperty("translate")] public bool Translate { get; set; }
}
