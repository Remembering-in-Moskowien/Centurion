using Centurion.Models.Asr;

namespace Centurion.Core.Capabilities.Infrastructure.Asr;

/// <summary>
/// Cloud ASR endpoint parser: resolves an engine/provider name into a provider,
/// endpoint and default model. Structurally mirrors the LLM side's
/// <c>LlmEndpointParser</c> — mainstream ASR APIs are compatible with OpenAI Whisper's
/// multipart transcription format (OpenAI/Groq/Alibaba Bailian), while Deepgram uses
/// its native protocol.
/// </summary>
public static class AsrEndpointParser
{
    /// <summary>OpenAI transcription endpoint.</summary>
    public const string OpenAiEndpoint = "https://api.openai.com/v1/audio/transcriptions";

    /// <summary>Groq transcription endpoint.</summary>
    public const string GroqEndpoint = "https://api.groq.com/openai/v1/audio/transcriptions";

    /// <summary>Alibaba Bailian DashScope OpenAI-compatible transcription endpoint.</summary>
    public const string DashScopeEndpoint = "https://dashscope.aliyuncs.com/api/v1/audio/transcriptions";

    /// <summary>Deepgram native endpoint.</summary>
    public const string DeepgramEndpoint = "https://api.deepgram.com/v1/listen";

    /// <summary>
    /// Resolves a name into a cloud ASR configuration; returns null for non-cloud engines
    /// (crispasr/whisper, etc.).
    /// </summary>
    /// <param name="engine">Transcription engine name, e.g. "openai", "groq", "dashscope"/"aliyun"/"qwen", "deepgram".</param>
    /// <returns>(provider, default endpoint, default model); null for non-cloud engines.</returns>
    public static (AsrProvider Provider, string Endpoint, string DefaultModel)? Resolve(string engine)
    {
        var normalized = engine.Trim().ToLowerInvariant();
        return normalized switch
        {
            "openai" or "open-ai" or "whisper-api" => (AsrProvider.OpenAI, OpenAiEndpoint, "whisper-1"),
            "groq" => (AsrProvider.Groq, GroqEndpoint, "whisper-large-v3"),
            "dashscope" or "aliyun" or "qwen" or "alibaba" or "bailian" => (AsrProvider.DashScope, DashScopeEndpoint, "paraformer-realtime-v2"),
            "deepgram" or "dg" => (AsrProvider.Deepgram, DeepgramEndpoint, "nova-2"),
            _ => null
        };
    }

    /// <summary>Checks whether an engine name is a cloud ASR.</summary>
    /// <param name="engine">Transcription engine name.</param>
    /// <returns>Whether it is a cloud provider.</returns>
    public static bool IsCloud(string engine) => Resolve(engine) is not null;

    /// <summary>List of supported cloud providers (used in error messages).</summary>
    public static string SupportedList => "openai, groq, dashscope, deepgram";
}
