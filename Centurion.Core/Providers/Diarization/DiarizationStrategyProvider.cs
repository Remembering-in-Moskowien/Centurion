using Centurion.Abstractions.Providers;
using Centurion.Abstractions.Strategy;
using Centurion.Models.Providers;

namespace Centurion.Core.Providers.Diarization;

/// <summary>
/// 说话人分割 Provider：适配 <see cref="IDiarizationStrategy"/>（CrispASR 内置 / Pyannote+TitaNet）。
/// 本地推理，免密钥；可用性以执行结果为准。
/// </summary>
public sealed class DiarizationStrategyProvider(
    string name,
    string displayName,
    IDiarizationStrategy strategy,
    ProviderCapabilities capabilities) : IDiarizationProvider
{
    private readonly IDiarizationStrategy _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));

    /// <inheritdoc />
    public string Name { get; } = name;

    /// <inheritdoc />
    public string DisplayName { get; } = displayName;

    /// <inheritdoc />
    public ProviderCapabilities Capabilities { get; } = capabilities;

    /// <inheritdoc />
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(true);

    /// <inheritdoc />
    public async Task<ProviderResult<IReadOnlyList<SpeakerSegment>>> DiarizeAsync(
        string audioPath, int numSpeakers, string? segmentModel, CancellationToken cancellationToken)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var segments = await _strategy.DiarizeAsync(audioPath, numSpeakers, segmentModel, cancellationToken,
            Centurion.Models.Workflow.InferenceDevice.Auto);
        sw.Stop();

        var usage = ProviderUsage.ForAudio(DisplayName, segmentModel, 0, 0, 0, sw.ElapsedMilliseconds);
        return new ProviderResult<IReadOnlyList<SpeakerSegment>>(segments, usage);
    }
}

/// <summary>说话人分割 Provider 注册工厂（名称/能力固定）。</summary>
public static class DiarizationProviders
{
    /// <summary>CrispASR 内置后端注册名。</summary>
    public const string CrispAsrName = "crispasr";

    /// <summary>Pyannote+TitaNet 后端注册名。</summary>
    public const string PyannoteName = "pyannote";

    /// <summary>CrispASR 能力声明：本地、energy/xcorr/vad-turns/foxnose 方法。</summary>
    public static ProviderCapabilities CrispAsrCapabilities => ProviderCapabilities.Local(
        requiresGpu: false,
        latency: ProviderLatency.Low,
        quality: ProviderQualityLevel.Normal,
        description: "CrispASR 内置说话人分割（energy/xcorr/vad-turns/foxnose）");

    /// <summary>Pyannote 能力声明：本地、高精度。</summary>
    public static ProviderCapabilities PyannoteCapabilities => ProviderCapabilities.Local(
        requiresGpu: true,
        latency: ProviderLatency.High,
        quality: ProviderQualityLevel.High,
        description: "Pyannote 分割 + TitaNet 嵌入（高精度，建议 GPU）");
}
