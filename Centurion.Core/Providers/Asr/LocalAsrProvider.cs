using Centurion.Abstractions.Providers;
using Centurion.Abstractions.Strategy;
using Centurion.Models;
using Centurion.Models.Providers;

namespace Centurion.Core.Providers.Asr;

/// <summary>
/// 本地 ASR Provider：适配现有本地转录策略（whisper.cpp / CrispASR-Qwen / CrispASR-Whisper）。
/// 执行形态为本地推理，免密钥、无按量成本；可用性以执行结果为准（失败由 fallback 链切换）。
/// </summary>
public sealed class LocalAsrProvider(
    string name,
    string displayName,
    ITranscriptionStrategy strategy,
    ProviderCapabilities capabilities) : IAsrProvider
{
    private readonly ITranscriptionStrategy _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));

    /// <inheritdoc />
    public string Name { get; } = name;

    /// <inheritdoc />
    public string DisplayName { get; } = displayName;

    /// <inheritdoc />
    public ProviderCapabilities Capabilities { get; } = capabilities;

    /// <inheritdoc />
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(true);

    /// <inheritdoc />
    public async Task<ProviderResult<IReadOnlyList<Word>>> TranscribeAsync(
        string audioPath, string language, string? model, string? initialPrompt, CancellationToken cancellationToken)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var words = await _strategy.TranscribeAsync(
            audioPath,
            language,
            string.IsNullOrWhiteSpace(model) ? "base" : model!,
            initialPrompt,
            cancellationToken,
            Centurion.Models.Workflow.InferenceDevice.Auto);
        sw.Stop();

        var usage = ProviderUsage.ForAudio(DisplayName, model, 0, 0, 0, sw.ElapsedMilliseconds);
        return new ProviderResult<IReadOnlyList<Word>>(words, usage);
    }
}

/// <summary>whisper.cpp 本地 Provider 的注册工厂（名称/能力固定）。</summary>
public static class WhisperCppAsrProvider
{
    /// <summary>注册名。</summary>
    public const string Name = "whispercpp";

    /// <summary>能力声明：本地、不限语言、低延迟、常规~高质量。</summary>
    public static ProviderCapabilities Capabilities => ProviderCapabilities.Local(
        requiresGpu: false,
        latency: ProviderLatency.Low,
        quality: ProviderQualityLevel.Normal,
        description: "whisper.cpp 本地转录（tiny/base/small/medium/large，GPU 感知选变体）");
}

/// <summary>CrispASR（Qwen3-ASR）本地 Provider 的注册工厂。</summary>
public static class CrispAsrQwenProvider
{
    /// <summary>注册名。</summary>
    public const string Name = "crispasr-qwen";

    /// <summary>能力声明：本地、不限语言、中低延迟、高质量。</summary>
    public static ProviderCapabilities Capabilities => ProviderCapabilities.Local(
        requiresGpu: false,
        latency: ProviderLatency.Medium,
        quality: ProviderQualityLevel.High,
        description: "Qwen3-ASR（CrispASR 后端）本地转录，词级时间戳，质量优先");
}

/// <summary>CrispASR（Whisper 后端）本地 Provider 的注册工厂。</summary>
public static class CrispAsrWhisperProvider
{
    /// <summary>注册名。</summary>
    public const string Name = "crispasr-whisper";

    /// <summary>能力声明：本地、常规质量。</summary>
    public static ProviderCapabilities Capabilities => ProviderCapabilities.Local(
        requiresGpu: false,
        latency: ProviderLatency.Medium,
        quality: ProviderQualityLevel.Normal,
        description: "CrispASR（Whisper 后端）本地转录");
}
