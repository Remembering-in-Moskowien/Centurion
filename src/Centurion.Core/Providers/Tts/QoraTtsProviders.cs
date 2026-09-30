using Centurion.Models.Providers;

namespace Centurion.Core.Providers.Tts;

/// <summary>QORA-TTS TTS Provider 的注册工厂。</summary>
public static class QoraTtsProviders
{
    /// <summary>注册名。</summary>
    public const string Name = "qora-tts";

    /// <summary>能力声明：本地、10 语言、音色克隆、CPU 推理。</summary>
    public static ProviderCapabilities Capabilities => ProviderCapabilities.Local(
        requiresGpu: false,
        latency: ProviderLatency.Medium,
        quality: ProviderQualityLevel.High,
        description: "QORA-TTS (Qwen3-TTS pure-Rust inference) local synthesis, 10-language voice cloning");
}
