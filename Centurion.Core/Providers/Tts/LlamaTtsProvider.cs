using Centurion.Abstractions.Providers;
using Centurion.Abstractions.Tts;
using Centurion.Models.Providers;

namespace Centurion.Core.Providers.Tts;

/// <summary>
/// TTS Provider：适配 <see cref="ITtsEngine"/>（llama.cpp llama-tts 本地合成）。
/// 免密钥、无按量成本；可用性以执行结果为准。
/// </summary>
public sealed class LlamaTtsProvider(
    string name,
    string displayName,
    ITtsEngine engine,
    ProviderCapabilities capabilities) : ITtsProvider
{
    private readonly ITtsEngine _engine = engine ?? throw new ArgumentNullException(nameof(engine));

    /// <inheritdoc />
    public string Name { get; } = name;

    /// <inheritdoc />
    public string DisplayName { get; } = displayName;

    /// <inheritdoc />
    public ProviderCapabilities Capabilities { get; } = capabilities;

    /// <inheritdoc />
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(true);

    /// <inheritdoc />
    public async Task<ProviderResult<double>> SynthesizeAsync(
        string text, string? referenceAudioPath, string language, string outputWavPath, CancellationToken cancellationToken)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var duration = await _engine.SynthesizeAsync(text, referenceAudioPath, language, outputWavPath, cancellationToken);
        sw.Stop();

        var usage = ProviderUsage.ForAudio(DisplayName, null, duration, 0, 0, sw.ElapsedMilliseconds);
        return new ProviderResult<double>(duration, usage);
    }
}

/// <summary>llama-tts TTS Provider 的注册工厂。</summary>
public static class LlamaTtsProviders
{
    /// <summary>注册名。</summary>
    public const string Name = "llama-tts";

    /// <summary>能力声明：本地、不限语言、高质量（音色克隆）。</summary>
    public static ProviderCapabilities Capabilities => ProviderCapabilities.Local(
        requiresGpu: false,
        latency: ProviderLatency.Medium,
        quality: ProviderQualityLevel.High,
        description: "llama.cpp llama-tts（Qwen3-TTS）本地合成，支持说话人参考音色");
}
