using Centurion.Core.Factories;
using Centurion.Models.Workflow;

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
    /// <summary>按设备创建 CrispASR 工具管理器的工厂。</summary>
    protected readonly IToolManagerFactory _toolManagerFactory;
    /// <summary>负责启动并管理外部 CLI 进程的执行器。</summary>
    protected readonly ProcessManager _processManager;
    /// <summary>用于解析模型文件本地路径的解析器。</summary>
    protected readonly IModelPathResolver _modelResolver;
    /// <summary>记录说话人分割过程日志的记录器。</summary>
    protected readonly ILogger _logger;
    private ToolManager? _toolManager;

    /// <summary>传给 --diarize-method 的方法名（energy/xcorr/vad-turns/foxnose/pyannote）。</summary>
    protected abstract string DiarizeMethod { get; }

    /// <summary>传给 --diarize-embedder 的嵌入器（"auto" = TitaNet；null = 不传）。</summary>
    protected virtual string? DiarizeEmbedder => null;

    /// <summary>pyannote 方法所需的分割模型名（如 "pyannote-seg-3.0"，由 CrispASR 自动下载）。</summary>
    protected virtual string? DefaultSegmentModel => null;

    /// <summary>从依赖注入容器解析所需服务，初始化基类共享依赖。</summary>
    /// <param name="serviceProvider">用于解析工具工厂、进程管理器、模型解析器与日志记录器的容器。</param>
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

    /// <summary>策略的显示名称。</summary>
    public abstract string StrategyName { get; }

    /// <summary>
    /// 对音频执行说话人分割：确保 CrispASR 就绪、解析模型、构建并运行 CLI，
    /// 解析输出 JSON 为说话人片段列表。
    /// </summary>
    /// <param name="audioPath">待分割音频文件路径。</param>
    /// <param name="numSpeakers">预期说话人数；大于 0 时作为最大说话人限制传入。</param>
    /// <param name="segmentModel">分割模型名；为 null 时回退到实现的默认模型。</param>
    /// <param name="cancellationToken">用于取消分割过程的取消标记。</param>
    /// <param name="device">推理设备，决定选用 CPU/GPU 变体工具。</param>
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

        // 2. 分割使用与主转录一致的 qwen3 模型（whisper tiny 分段边界不稳定，
        //    导致说话人片段每次运行漂移；同模型同后端输出更确定）
        var modelPath = await _modelResolver.GetQwen3AsrModelPathAsync("qwen3-asr-1.7b", cancellationToken);
        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"Qwen3 ASR model not found: {modelPath}");

        // 3. 构建参数并执行（--diarize-speakers 将说话人标签写入 transcription 条目的 speaker 字段）
        var jsonBasePath = Path.Combine(
            Path.GetDirectoryName(audioPath) ?? string.Empty,
            Path.GetFileNameWithoutExtension(audioPath) + "_diar");
        var args = BuildArguments(audioPath, modelPath, jsonBasePath, numSpeakers, segmentModel, DiarizeMethod, DiarizeEmbedder, DefaultSegmentModel, backend: "qwen3");

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
    /// 使用 --diarize-speakers 使说话人标签写入 transcription 条目的 speaker 字段；
    /// --diarize-method 仍可选用 foxnose（WeSpeaker 嵌入）或 pyannote（TitaNet 嵌入）。
    /// 分割模型（如 pyannote-seg-3.0.gguf）由 CrispASR 自动下载并缓存，无需 --sherpa-segment-model。
    /// </summary>
    internal static string BuildArguments(
        string audioPath,
        string modelPath,
        string jsonBasePath,
        int numSpeakers,
        string? segmentModel,
        string method,
        string? embedder,
        string? defaultSegmentModel,
        string backend = "qwen3")
    {
        _ = segmentModel;
        _ = defaultSegmentModel;
        var args = $"--backend {backend} -m \"{modelPath}\" -f \"{audioPath}\" " +
                   $"--diarize-speakers --diarize-method {method} -ojf -of \"{jsonBasePath}\"";
        if (!string.IsNullOrEmpty(embedder))
            args += $" --diarize-embedder {embedder}";
        if (numSpeakers > 0)
            args += $" --diarize-max-speakers {numSpeakers}";
        return args;
    }

    /// <summary>
    /// 使用当前实现的方法与嵌入器构建 CLI 参数（供派生类调用）。
    /// </summary>
    /// <param name="audioPath">待分割音频文件路径。</param>
    /// <param name="modelPath">CrispASR CLI 所需的 ASR 模型路径。</param>
    /// <param name="jsonBasePath">输出 JSON 的基础路径（不含扩展名）。</param>
    /// <param name="numSpeakers">预期说话人数；大于 0 时作为最大说话人限制。</param>
    /// <param name="segmentModel">预留的分割模型名（由 CrispASR 自动下载管理，当前不参与命令行）。</param>
    protected string BuildArguments(
        string audioPath, string modelPath, string jsonBasePath, int numSpeakers, string? segmentModel) =>
        BuildArguments(audioPath, modelPath, jsonBasePath, numSpeakers, segmentModel,
            DiarizeMethod, DiarizeEmbedder, DefaultSegmentModel);
}

