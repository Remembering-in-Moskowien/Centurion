using Centurion.Models.Providers;

namespace Centurion.Core.Providers.Tts;

/// <summary>IndexTTS-Rust TTS Provider 的注册工厂。</summary>
public static class IndexTtsProviders
{
    /// <summary>注册名。</summary>
    public const string Name = "indextts";

    /// <summary>能力声明：本地、中英混合、零样本音色克隆（上游推理当前为占位实现）。</summary>
    public static ProviderCapabilities Capabilities => ProviderCapabilities.Local(
        requiresGpu: false,
        latency: ProviderLatency.Medium,
        quality: ProviderQualityLevel.Normal,
        description: "IndexTTS-Rust (ONNX) local synthesis, zero-shot voice cloning");
}
