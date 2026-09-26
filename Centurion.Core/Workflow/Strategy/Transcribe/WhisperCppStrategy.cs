using Centurion.Core.Workflow.Factories;using Centurion.Models.Workflow;

using Centurion.Abstractions;
using Centurion.Abstractions.Factories;
using Centurion.Abstractions.Strategy;
using Centurion.Models;
using Centurion.Models.Transcript;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Centurion.Core.Capabilities.Managers.Runtime;
using Centurion.Core.Capabilities.Managers.Tools;
using Centurion.Core.Utils.Parsing;
namespace Centurion.Core.Workflow.Strategy.Transcribe;

/// <summary>
/// 基于 whisper.cpp 的转录策略：调用 whisper.cpp 可执行文件转录音频，
/// 解析其 JSON 输出提取词级时间戳。
/// </summary>
public class WhisperCppStrategy(IServiceProvider serviceProvider) : ITranscriptionStrategy
{
    private readonly IToolManagerFactory _toolManagerFactory = serviceProvider.GetRequiredService<IToolManagerFactory>();
    private readonly ProcessManager _processManager = serviceProvider.GetRequiredService<ProcessManager>();
    private readonly IModelPathResolver _modelResolver = serviceProvider.GetRequiredService<IModelPathResolver>();
    private readonly ILogger<WhisperCppStrategy> _logger = serviceProvider.GetRequiredService<ILogger<WhisperCppStrategy>>();
    private ToolManager? _toolManager;

    /// <summary>策略的显示名称。</summary>
    public string StrategyName => "Whisper.cpp";

    /// <summary>按推理设备创建（懒加载）whisper.cpp 工具管理器（GPU 可用时自动选用 CUDA 构建）。</summary>
    private ToolManager GetToolManager(InferenceDevice device) =>
        _toolManager ??= _toolManagerFactory.Create("whispercpp", device);

    /// <summary>
    /// 执行转录：确保 whisper.cpp 工具就绪、解析模型、构建并运行 CLI，
    /// 读取输出 JSON 提取词级时间戳。
    /// </summary>
    /// <param name="audioPath">待转录音频文件路径。</param>
    /// <param name="language">音频语言代码。</param>
    /// <param name="modelName">转录模型名。</param>
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
        // 1. 确保工具已下载（按设备选择 GPU/CPU 变体）
        var toolManager = GetToolManager(device);
        await toolManager.EnsureToolAsync(cancellationToken);

        // 2. 获取模型文件路径
        var modelPath = await _modelResolver.GetWhisperModelPathAsync(modelName, cancellationToken);
        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"Whisper model file not found: {modelPath}");

        // 3. 构建参数数组：使用 -oj 强制输出 JSON，输出文件自动生成在音频同目录下
        //    语言为空时不传 -l，由模型自动检测（对中文等非英语音更稳健）
        //    使用 ArgumentList 传递，避免路径/提示词中的引号破坏参数边界
        var args = new List<string> { "-m", modelPath, "-f", audioPath, "-oj" };
        if (!string.IsNullOrWhiteSpace(language))
        {
            args.Add("-l");
            args.Add(language);
        }
        if (!string.IsNullOrEmpty(initialPrompt))
        {
            args.Add("-p");
            args.Add(initialPrompt);
        }

        _logger.LogDebug("Executing: {Exe} {Args}", toolManager.ExecutablePath, string.Join(' ', args));

        // 4. 执行进程（输出会生成 JSON 文件，标准输出可能只是进度或日志）
        var output = await _processManager.ExecuteAsync(
            toolManager.ExecutablePath,
            args,
            cancellationToken: cancellationToken);

        // 5. 确定 JSON 文件路径（与音频同目录同文件名，扩展名为 .json）
        var jsonPath = Path.ChangeExtension(audioPath, ".json");
        if (!File.Exists(jsonPath))
            throw new InvalidOperationException($"Whisper.cpp did not produce expected JSON file: {jsonPath}");

        // 6. 读取并解析 JSON
        var jsonContent = File.ReadAllText(jsonPath);
        var whisperResult = JsonParser.Deserialize<WhisperTranscriptJson>(jsonContent);

        // 7. 提取词级信息
        return ExtractWords(whisperResult);
    }

    /// <summary>
    /// 从 WhisperTranscriptJson 中提取词级时间戳。
    /// 优先使用 Token 级别的词信息（最精确），否则回退到段级别并尝试按空格拆分（会丢失精确时间）。
    /// </summary>
    private List<Word> ExtractWords(WhisperTranscriptJson result)
    {
        var words = new List<Word>();

        foreach (var item in result.Transcription)
        {
            // 优先使用 tokens（词级）
            if (item.Tokens != null && item.Tokens.Count > 0)
            {
                foreach (var token in item.Tokens)
                {
                    var text = token.Text?.Trim() ?? string.Empty;
                    if (string.IsNullOrEmpty(text))
                        continue;

                    // 跳过特殊标记（如空白、标点等），可根据需要过滤
                    // 这里保留所有非空 token
                    words.Add(new Word
                    {
                        Text = text,
                        Start = token.Offsets.From / 1000.0,
                        End = token.Offsets.To / 1000.0,
                        Speaker = "SPEAKER_00" // 后续由说话人分割填充
                    });
                }
            }
            else
            {
                // 降级方案：使用段级别，将文本按空格拆分为单词，并平均分配时间段（不精确，不推荐）
                var segmentText = item.Text?.Trim() ?? string.Empty;
                if (string.IsNullOrEmpty(segmentText))
                    continue;

                var parts = segmentText.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                    continue;

                var segmentStart = item.Offsets.From / 1000.0;
                var segmentEnd = item.Offsets.To / 1000.0;
                var duration = segmentEnd - segmentStart;
                var avgDuration = duration / parts.Length;

                for (var i = 0; i < parts.Length; i++)
                {
                    words.Add(new Word
                    {
                        Text = parts[i],
                        Start = segmentStart + i * avgDuration,
                        End = segmentStart + (i + 1) * avgDuration,
                        Speaker = "SPEAKER_00"
                    });
                }
            }
        }

        return words;
    }
}
