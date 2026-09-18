using Centurion.Core.Factories;
using Centurion.Models.Workflow;
// Centurion.Core/Strategy/Diarization/CrispAsrDiarizationBase.cs

using Centurion.Abstractions;
using Centurion.Abstractions.Factories;
using Centurion.Abstractions.Strategy;
using Centurion.Abstractions.Exceptions;
using Centurion.Core.Managers;
using Centurion.Core.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Strategy.Diarization;

/// <summary>
/// CrispASR 说话人分割策略基类。
/// 两类方案（crispasr 内置方法 / Pyannote + TitaNet）共用同一 CLI 调用与 JSON 解析流程，
/// 仅命令行参数不同（方法、嵌入器、分割模型）。
/// </summary>
public abstract class CrispAsrDiarizationBase : IDiarizationStrategy
{
    protected readonly IToolManagerFactory _toolManagerFactory;
    protected readonly ProcessManager _processManager;
    protected readonly IModelPathResolver _modelResolver;
    protected readonly ILogger _logger;
    private ToolManager? _toolManager;

    /// <summary>传给 --diarize-method 的方法名（energy/xcorr/vad-turns/foxnose/pyannote）。</summary>
    protected abstract string DiarizeMethod { get; }

    /// <summary>传给 --diarize-embedder 的嵌入器（"auto" = TitaNet；null = 不传）。</summary>
    protected virtual string? DiarizeEmbedder => null;

    /// <summary>pyannote 方法所需的分割模型名（如 "pyannote-seg-3.0"，由 CrispASR 自动下载）。</summary>
    protected virtual string? DefaultSegmentModel => null;

    protected CrispAsrDiarizationBase(IServiceProvider serviceProvider)
    {
        _toolManagerFactory = serviceProvider.GetRequiredService<IToolManagerFactory>();
        _processManager = serviceProvider.GetRequiredService<ProcessManager>();
        _modelResolver = serviceProvider.GetRequiredService<IModelPathResolver>();
        _logger = serviceProvider.GetRequiredService<ILogger<CrispAsrDiarizationBase>>();
    }

    /// <summary>按推理设备创建（懒加载）CrispASR 工具管理器。</summary>
    protected ToolManager GetToolManager(InferenceDevice device) =>
        _toolManager ??= _toolManagerFactory.Create("crispasr", device);

    public abstract string StrategyName { get; }

    public virtual async Task<IReadOnlyList<SpeakerSegment>> DiarizeAsync(
        string audioPath,
        int numSpeakers,
        string? segmentModel,
        CancellationToken cancellationToken = default,
        InferenceDevice device = InferenceDevice.Auto)
    {
        if (!File.Exists(audioPath))
            throw new FileNotFoundException($"Audio file not found: {audioPath}");

        // 1. 确保 CrispASR 工具已就绪（GPU 变体按设备自动选择）
        var toolManager = GetToolManager(device);
        await toolManager.EnsureToolAsync(cancellationToken);

        // 2. CLI 需要 -m 指定一个 ASR 模型；diarization 本身不依赖其质量，使用最小 whisper 模型
        var modelPath = await _modelResolver.GetWhisperModelPathAsync("tiny", cancellationToken);
        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"Whisper tiny model not found: {modelPath}");

        // 3. 构建参数并执行（-ojf 输出 diarized JSON）
        var jsonBasePath = Path.Combine(
            Path.GetDirectoryName(audioPath) ?? string.Empty,
            Path.GetFileNameWithoutExtension(audioPath) + "_diar");
        var args = BuildArguments(audioPath, modelPath, jsonBasePath, numSpeakers, segmentModel);

        _logger.LogDebug("Executing diarization: {Exe} {Args}", toolManager.ExecutablePath, args);
        await _processManager.ExecuteAsync(toolManager.ExecutablePath, args, cancellationToken);

        // 4. 读取并解析输出
        var jsonPath = jsonBasePath + ".json";
        if (!File.Exists(jsonPath))
            throw new DiarizationException($"Diarization output JSON not found at: {jsonPath}");

        var json = await File.ReadAllTextAsync(jsonPath, cancellationToken);
        return DiarizationJsonParser.Parse(json);
    }

    /// <summary>
    /// 构建 CrispASR 说话人分割命令行参数（internal，便于单元测试）。
    /// </summary>
    internal static string BuildArguments(
        string audioPath,
        string modelPath,
        string jsonBasePath,
        int numSpeakers,
        string? segmentModel,
        string method,
        string? embedder,
        string? defaultSegmentModel)
    {
        var effectiveSegmentModel = segmentModel ?? defaultSegmentModel;
        var args = $"--backend whisper -m \"{modelPath}\" -f \"{audioPath}\" " +
                   $"--diarize --diarize-method {method} -ojf -of \"{jsonBasePath}\"";
        if (!string.IsNullOrEmpty(embedder))
            args += $" --diarize-embedder {embedder}";
        if (!string.IsNullOrEmpty(effectiveSegmentModel))
            args += $" --sherpa-segment-model {effectiveSegmentModel}";
        if (numSpeakers > 0)
            args += $" --diarize-max-speakers {numSpeakers}";
        return args;
    }

    protected string BuildArguments(
        string audioPath, string modelPath, string jsonBasePath, int numSpeakers, string? segmentModel) =>
        BuildArguments(audioPath, modelPath, jsonBasePath, numSpeakers, segmentModel,
            DiarizeMethod, DiarizeEmbedder, DefaultSegmentModel);
}
