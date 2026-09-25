namespace Centurion.Models.Asr;

/// <summary>
/// 云端 ASR 提供商。
/// </summary>
public enum AsrProvider
{
    /// <summary>OpenAI Whisper API。</summary>
    OpenAI,

    /// <summary>Groq Whisper API（whisper-large-v3）。</summary>
    Groq,

    /// <summary>阿里百炼 DashScope ASR（Paraformer / SenseVoice，OpenAI 兼容）。</summary>
    DashScope,

    /// <summary>Deepgram ASR（nova-2，原生协议）。</summary>
    Deepgram
}
