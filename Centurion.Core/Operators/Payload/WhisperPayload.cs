namespace Centurion.Core.Operators.Payload;

/// <summary>Whisper 转录请求载荷，统一承载所有入参</summary>
public class WhisperTranscribePayload
{
    /// <summary>输入音频文件路径</summary>
    public string FilePath { get; init; } = null!;

    /// <summary>识别语言，默认 en</summary>
    public string Language { get; init; } = "en";

    /// <summary>
    /// 输入的Whisper提示词
    /// </summary>
    public string? InitialPrompt { get; init; } // 新增
}