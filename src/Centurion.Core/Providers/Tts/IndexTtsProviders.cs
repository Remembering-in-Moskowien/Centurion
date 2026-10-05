using Centurion.Models.Providers;

namespace Centurion.Core.Providers.Tts;

/// <summary>Registration factory for the IndexTTS-Rust TTS provider.</summary>
public static class IndexTtsProviders
{
    /// <summary>Registered name.</summary>
    public const string Name = "indextts";

    /// <summary>Capability declaration: local, mixed Chinese/English, zero-shot voice cloning (upstream inference is currently a placeholder implementation).</summary>
    public static ProviderCapabilities Capabilities => ProviderCapabilities.Local(
        requiresGpu: false,
        latency: ProviderLatency.Medium,
        quality: ProviderQualityLevel.Normal,
        description: "IndexTTS-Rust (ONNX) local synthesis, zero-shot voice cloning");
}
