namespace Centurion.Core.Operators.Results;

using System.Collections.Generic;
using Newtonsoft.Json;

// 根对象
public class WhisperTranscriptResult
{
    [JsonProperty("systeminfo")]
    public required string SystemInfo { get; set; }

    [JsonProperty("model")]
    public required ModelInfo Model { get; set; }

    [JsonProperty("params")]
    public required ParamsInfo Params { get; set; }

    [JsonProperty("result")]
    public required ResultInfo Result { get; set; }

    [JsonProperty("transcription")]
    public required List<TranscriptionItem> Transcription { get; set; }
}

// model 节点
public class ModelInfo
{
    [JsonProperty("type")]
    public required string Type { get; set; }

    [JsonProperty("multilingual")]
    public bool Multilingual { get; set; }

    [JsonProperty("vocab")]
    public int Vocab { get; set; }

    [JsonProperty("audio")]
    public required AudioInfo Audio { get; set; }

    [JsonProperty("text")]
    public required TextInfo Text { get; set; }

    [JsonProperty("mels")]
    public int Mels { get; set; }

    [JsonProperty("ftype")]
    public int Ftype { get; set; }
}

public class AudioInfo
{
    [JsonProperty("ctx")]
    public int Ctx { get; set; }

    [JsonProperty("state")]
    public int State { get; set; }

    [JsonProperty("head")]
    public int Head { get; set; }

    [JsonProperty("layer")]
    public int Layer { get; set; }
}

public class TextInfo
{
    [JsonProperty("ctx")]
    public int Ctx { get; set; }

    [JsonProperty("state")]
    public int State { get; set; }

    [JsonProperty("head")]
    public int Head { get; set; }

    [JsonProperty("layer")]
    public int Layer { get; set; }
}

// params 节点
public class ParamsInfo
{
    [JsonProperty("model")]
    public required string Model { get; set; }

    [JsonProperty("language")]
    public required string Language { get; set; }

    [JsonProperty("translate")]
    public bool Translate { get; set; }
}

// result 节点
public class ResultInfo
{
    [JsonProperty("language")]
    public required string Language { get; set; }
}

// transcription 数组子项（支持词级时间戳和置信度）
public class TranscriptionItem
{
    [JsonProperty("timestamps")]
    public required TimeStampInfo Timestamps { get; set; }

    [JsonProperty("offsets")]
    public required OffsetInfo Offsets { get; set; }

    [JsonProperty("text")]
    public required string Text { get; set; }

    [JsonProperty("tokens")]
    public List<TokenInfo>? Tokens { get; set; }  // 当使用 --output-json-full 时包含此项
}

// 时间戳信息（用于段或词）
public class TimeStampInfo
{
    [JsonProperty("from")]
    public required string From { get; set; }

    [JsonProperty("to")]
    public required string To { get; set; }
}

// 偏移信息（毫秒，用于段或词）
public class OffsetInfo
{
    [JsonProperty("from")]
    public int From { get; set; }

    [JsonProperty("to")]
    public int To { get; set; }
}

// 词级 Token 信息（包含置信度）
public class TokenInfo
{
    [JsonProperty("text")]
    public required string Text { get; set; }

    [JsonProperty("timestamps")]
    public required TimeStampInfo Timestamps { get; set; }

    [JsonProperty("offsets")]
    public required OffsetInfo Offsets { get; set; }

    [JsonProperty("id")]
    public int Id { get; set; }

    [JsonProperty("p")]
    public float P { get; set; }  // 置信度（概率）

    [JsonProperty("t_dtw")]
    public int Tdtw { get; set; }
    
    public bool IsSpecialToken()
    {
        // 判断文本是否以 "[_" 开头并以 "]" 结尾，且包含特定标识
        return Text.StartsWith("[_") && Text.EndsWith("]") && 
               (Text.Contains("_BEG_") || Text.Contains("_TT_") || 
                Text.Contains("_EOT_") || Text.Contains("_SOT_"));
    }
}