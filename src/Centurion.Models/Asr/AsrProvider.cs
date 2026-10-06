namespace Centurion.Models.Asr;

/// <summary>
/// Cloud ASR provider.
/// </summary>
public enum AsrProvider
{
    /// <summary>OpenAI Whisper API.</summary>
    OpenAI,

    /// <summary>Groq Whisper API (whisper-large-v3).</summary>
    Groq,

    /// <summary>Alibaba DashScope ASR (Paraformer / SenseVoice, OpenAI-compatible).</summary>
    DashScope,

    /// <summary>Deepgram ASR (nova-2, native protocol).</summary>
    Deepgram
}
