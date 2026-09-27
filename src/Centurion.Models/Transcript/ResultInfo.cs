using Newtonsoft.Json;

namespace Centurion.Models.Transcript;

// result 节点
/// <summary>whisper.cpp 输出 JSON 中 result 节点的识别结果摘要。</summary>
public class ResultInfo
{
    /// <summary>实际检测到的语言代码。</summary>
    [JsonProperty("language")] public required string Language { get; set; }
}
