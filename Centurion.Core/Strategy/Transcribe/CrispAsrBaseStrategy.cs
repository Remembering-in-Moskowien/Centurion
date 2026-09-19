using Centurion.Core.Factories;
using Centurion.Models.Workflow;

using Centurion.Abstractions;
using Centurion.Abstractions.Factories;
using Centurion.Abstractions.Strategy;
using Centurion.Core.Managers;
using Centurion.Models;
using Centurion.Models.Transcript;
using Centurion.Core.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Strategy.Transcribe;

/// <summary>
/// Base strategy for CrispASR with different backends (Qwen3, Whisper, etc.)
/// </summary>
public abstract class CrispAsrBaseStrategy : ITranscriptionStrategy
{
    /// <summary>按设备创建 CrispASR 工具管理器的工厂。</summary>
    protected readonly IToolManagerFactory _toolManagerFactory;
    /// <summary>负责启动并管理外部 CLI 进程的执行器。</summary>
    protected readonly ProcessManager _processManager;
    /// <summary>用于解析模型文件本地路径的解析器。</summary>
    protected readonly IModelPathResolver _modelResolver;
    /// <summary>记录转录过程日志的记录器。</summary>
    protected readonly ILogger<CrispAsrBaseStrategy> _logger;
    private ToolManager? _toolManager;

    /// <summary>策略的显示名称。</summary>
    public abstract string StrategyName { get; }

    /// <summary>从依赖注入容器解析所需服务，初始化基类共享依赖。</summary>
    /// <param name="serviceProvider">用于解析工具工厂、进程管理器、模型解析器与日志记录器的容器。</param>
    protected CrispAsrBaseStrategy(IServiceProvider serviceProvider)
    {
        _toolManagerFactory = serviceProvider.GetRequiredService<IToolManagerFactory>();
        _processManager = serviceProvider.GetRequiredService<ProcessManager>();
        _modelResolver = serviceProvider.GetRequiredService<IModelPathResolver>();
        _logger = serviceProvider.GetRequiredService<ILogger<CrispAsrBaseStrategy>>();
    }

    /// <summary>按推理设备创建（懒加载）CrispASR 工具管理器。</summary>
    protected ToolManager GetToolManager(InferenceDevice device) =>
        _toolManager ??= _toolManagerFactory.Create("crispasr", device);

    /// <summary>
    /// Backend name to pass to CrispASR (e.g., "qwen3", "whisper")
    /// </summary>
    protected abstract string GetBackendName();

    /// <summary>
    /// Resolve the model file path for the given model name.
    /// </summary>
    protected abstract Task<string> GetModelPathAsync(string modelName, CancellationToken cancellationToken);

    /// <summary>
    /// Optionally resolve an aligner model path; return null if not used.
    /// </summary>
    protected virtual Task<string?> GetAlignerPathAsync(CancellationToken cancellationToken)
        => Task.FromResult<string?>(null);

    /// <summary>
    /// Build the command-line arguments. Override if needed.
    /// 返回参数列表（不含引号），由 <see cref="ProcessManager"/> 以 ArgumentList 方式
    /// 安全传递，避免路径/提示词中的引号破坏参数边界。
    /// </summary>
    protected virtual IReadOnlyList<string> BuildArguments(string audioPath, string language, string modelPath, string? alignerPath, string? initialPrompt)
    {
        // Determine output JSON base path (same base as audio)
        var jsonOutputPath = Path.ChangeExtension(audioPath, ".json");
        var jsonBasePath = Path.Combine(
            Path.GetDirectoryName(jsonOutputPath) ?? string.Empty,
            Path.GetFileNameWithoutExtension(jsonOutputPath));

        var args = new List<string>
        {
            "--backend", GetBackendName(),
            "-m", modelPath,
            "-f", audioPath,
            "-ojf",
            "-of", jsonBasePath
        };
        if (!string.IsNullOrEmpty(language))
        {
            args.Add("-l");
            args.Add(language);
        }
        if (!string.IsNullOrEmpty(alignerPath))
        {
            args.Add("-am");
            args.Add(alignerPath);
        }
        if (!string.IsNullOrEmpty(initialPrompt))
        {
            args.Add("--prompt");
            args.Add(initialPrompt);
        }
        return args;
    }

    /// <summary>
    /// 执行转录：确保 CrispASR 就绪、解析模型与可选对齐器、构建并运行 CLI，
    /// 解析输出 JSON 为词级时间戳列表。
    /// </summary>
    /// <param name="audioPath">待转录音频文件路径。</param>
    /// <param name="language">音频语言代码；为空时由模型自动判断。</param>
    /// <param name="modelName">转录模型名；为空时使用实现的默认模型。</param>
    /// <param name="initialPrompt">可选的初始提示词。</param>
    /// <param name="cancellationToken">用于取消转录过程的取消标记。</param>
    /// <param name="device">推理设备，决定选用 CPU/GPU 变体工具。</param>
    public async Task<List<Word>> TranscribeAsync(
        string audioPath,
        string language,
        string modelName,
        string? initialPrompt = null,
        CancellationToken cancellationToken = default,
        InferenceDevice device = InferenceDevice.Auto)
    {
        // 1. Ensure CrispASR tool is downloaded（GPU 变体按设备自动选择）
        var toolManager = GetToolManager(device);
        await toolManager.EnsureToolAsync(cancellationToken);

        // 2. Get model path
        var modelPath = await GetModelPathAsync(modelName, cancellationToken);
        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"Model file not found: {modelPath}");

        // 3. Get aligner if available
        var alignerPath = await GetAlignerPathAsync(cancellationToken);
        if (alignerPath != null && !File.Exists(alignerPath))
        {
            _logger.LogWarning("Aligner model not found at {Path}. Alignment will be skipped.", alignerPath);
            alignerPath = null;
        }

        // 4. Build arguments
        var args = BuildArguments(audioPath, language, modelPath, alignerPath, initialPrompt);
        _logger.LogDebug("Executing CrispASR: {Exe} {Args}", toolManager.ExecutablePath, string.Join(' ', args));

        // 5. Execute process
        await _processManager.ExecuteAsync(toolManager.ExecutablePath, args, cancellationToken: cancellationToken);

        // 6. Read generated JSON
        var jsonOutputPath = Path.ChangeExtension(audioPath, ".json");
        if (!File.Exists(jsonOutputPath))
            throw new FileNotFoundException($"CrispASR output JSON not found at: {jsonOutputPath}");

        var json = await File.ReadAllTextAsync(jsonOutputPath, cancellationToken);

        // 7. Parse
        return ParseJsonOutput(json);
    }

    /// <summary>
    /// 解析 CrispASR 输出 JSON（实体模型反序列化），提取词级时间戳列表。
    /// 同时兼容 whisper 与 qwen3 后端：两者均输出 words（text/offsets），仅 tokens 时间戳可能缺失。
    /// </summary>
    /// <param name="json">CrispASR -ojf 格式的 JSON 字符串。</param>
    /// <returns>解析得到的词级时间戳列表。</returns>
    private List<Word> ParseJsonOutput(string json)
    {
        var root = JsonParser.Deserialize<CrispAsrTranscriptJson>(json);

        if (root.Transcription is null)
            throw new InvalidOperationException("Missing 'transcription' array in CrispASR JSON output.");

        var words = new List<Word>();
        foreach (var segment in root.Transcription)
        {
            if (segment.Words is null)
                continue;

            foreach (var wordElement in segment.Words)
            {
                var text = wordElement.Text;
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                words.Add(new Word
                {
                    Text = text,
                    Start = wordElement.Offsets.From,
                    End = wordElement.Offsets.To,
                    Speaker = "SPEAKER_00"
                });
            }
        }

        if (words.Count == 0)
            _logger.LogWarning("No words were parsed from the JSON output.");

        return words;
    }
}
