using Centurion.Abstractions;
using Centurion.Core.Workflow.Factories;
using Centurion.Abstractions.Providers;
using Centurion.Core.Capabilities.Managers.Runtime;
using Centurion.Core.Workflow.Pipeline.Operators;
using Centurion.Models.Providers;

namespace Centurion.Core.Providers.VocalSeparation;

/// <summary>
/// 人声分离 Provider：直接驱动 demucs-rs CLI（-s vocals）分离人声轨。
/// 轻量路径：工具按需下载、模型名默认 htdemucs（demucs-rs 自身缓存）；
/// 管道内 <see cref="VocalSeparationOperator"/> 为生产路径（含镜像预下载增强）。
/// </summary>
public sealed class DemucsVocalSeparationProvider(
    string name,
    string displayName,
    IToolManagerFactory toolFactory,
    ProcessManager processManager,
    ProviderCapabilities capabilities) : IVocalSeparationProvider
{
    /// <summary>默认分离模型。</summary>
    public string DefaultModel { get; set; } = "htdemucs";

    /// <inheritdoc />
    public string Name { get; } = name;

    /// <inheritdoc />
    public string DisplayName { get; } = displayName;

    /// <inheritdoc />
    public ProviderCapabilities Capabilities { get; } = capabilities;

    /// <inheritdoc />
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(true);

    /// <inheritdoc />
    public async Task<ProviderResult<string>> SeparateVocalsAsync(
        string audioPath, string outputWavPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(audioPath))
            throw new FileNotFoundException($"Audio file not found: {audioPath}", audioPath);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var tool = toolFactory.Create("demucsrs", Centurion.Models.Workflow.InferenceDevice.Auto);
        await tool.EnsureToolAsync(cancellationToken);

        var outputDir = Path.Combine(Path.GetDirectoryName(outputWavPath) ?? Path.GetTempPath(),
            $"vocalsep_provider_{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputDir);

        var args = VocalSeparationOperator.BuildArguments(DefaultModel, audioPath, outputDir);
        await processManager.ExecuteAsync(tool.ExecutablePath, args, cancellationToken);

        var vocalsFile = VocalSeparationOperator.FindVocalsFile(outputDir);
        if (vocalsFile is null)
            throw new ProviderExecutionException("Demucs finished but no 'vocals' stem was found.", Name);

        if (!string.Equals(Path.GetFullPath(vocalsFile), Path.GetFullPath(outputWavPath), StringComparison.OrdinalIgnoreCase))
            File.Copy(vocalsFile, outputWavPath, overwrite: true);

        sw.Stop();
        var usage = ProviderUsage.ForAudio(DisplayName, DefaultModel, 0, 0, 0, sw.ElapsedMilliseconds);
        return new ProviderResult<string>(outputWavPath, usage);
    }
}

/// <summary>Demucs 人声分离 Provider 注册工厂。</summary>
public static class DemucsVocalSeparationProviders
{
    /// <summary>注册名。</summary>
    public const string Name = "demucs";

    /// <summary>能力声明：本地、不限语言、高质量分离。</summary>
    public static ProviderCapabilities Capabilities => ProviderCapabilities.Local(
        requiresGpu: true,
        latency: ProviderLatency.High,
        quality: ProviderQualityLevel.High,
        description: "demucs-rs（htdemucs）本地人声分离，建议 GPU");
}
