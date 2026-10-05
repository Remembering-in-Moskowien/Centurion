using Centurion.Models.Providers;

namespace Centurion.Core.Providers.Tts;

/// <summary>Registration factory for the QORA-TTS TTS provider.</summary>
public static class QoraTtsProviders
{
    /// <summary>Registered name.</summary>
    public const string Name = "qora-tts";

    /// <summary>Capability declaration: local, 10 languages, voice cloning, CPU inference.</summary>
    public static ProviderCapabilities Capabilities => ProviderCapabilities.Local(
        requiresGpu: false,
        latency: ProviderLatency.Medium,
        quality: ProviderQualityLevel.High,
        description: "QORA-TTS (Qwen3-TTS pure-Rust inference) local synthesis, 10-language voice cloning");
}
