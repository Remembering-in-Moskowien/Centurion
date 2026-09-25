using Centurion.Models.Asr;

namespace Centurion.Core.Asr;

/// <summary>
/// 云端 ASR 端点解析器：把引擎/提供商名称解析为提供商、端点与默认模型。
/// 与 LLM 的 <c>LlmEndpointParser</c> 同构——主流 ASR API 均兼容 OpenAI Whisper
/// 的 multipart 转录格式（OpenAI/Groq/阿里百炼），Deepgram 使用原生协议。
/// </summary>
public static class AsrEndpointParser
{
    /// <summary>OpenAI 转录端点。</summary>
    public const string OpenAiEndpoint = "https://api.openai.com/v1/audio/transcriptions";

    /// <summary>Groq 转录端点。</summary>
    public const string GroqEndpoint = "https://api.groq.com/openai/v1/audio/transcriptions";

    /// <summary>阿里百炼 DashScope OpenAI 兼容转录端点。</summary>
    public const string DashScopeEndpoint = "https://dashscope.aliyuncs.com/api/v1/audio/transcriptions";

    /// <summary>Deepgram 原生端点。</summary>
    public const string DeepgramEndpoint = "https://api.deepgram.com/v1/listen";

    /// <summary>
    /// 按名称解析为云端 ASR 配置；非云端引擎（crispasr/whisper 等）返回 null。
    /// </summary>
    /// <param name="engine">转录引擎名，如 "openai"、"groq"、"dashscope"/"aliyun"/"qwen"、"deepgram"。</param>
    /// <returns>（提供商, 默认端点, 默认模型）；非云端引擎返回 null。</returns>
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

    /// <summary>判断引擎名是否为云端 ASR。</summary>
    /// <param name="engine">转录引擎名。</param>
    /// <returns>是否为云端提供商。</returns>
    public static bool IsCloud(string engine) => Resolve(engine) is not null;

    /// <summary>支持的云端提供商列表（用于错误提示）。</summary>
    public static string SupportedList => "openai, groq, dashscope, deepgram";
}
