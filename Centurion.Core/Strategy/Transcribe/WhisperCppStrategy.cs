// Centurion.Core/Strategies/Transcription/WhisperCppStrategy.cs

using Centurion.Core.Abstractions;
using Centurion.Core.Abstractions.Strategy;
using Centurion.Core.Managers;
using Centurion.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Centurion.Core.Strategy.Transcribe;

public class WhisperCppStrategy(IServiceProvider serviceProvider) : ITranscriptionStrategy
{
    private readonly ToolManager _toolManager = new("whispercpp", serviceProvider);
    private readonly ProcessManager _processManager = new(serviceProvider.GetRequiredService<ILogger<ProcessManager>>());
    private readonly IModelPathResolver _modelResolver = serviceProvider.GetRequiredService<IModelPathResolver>();
    private readonly ILogger<WhisperCppStrategy> _logger = serviceProvider.GetRequiredService<ILogger<WhisperCppStrategy>>();

    public string StrategyName => "Whisper.cpp";

    public async Task<List<Word>> TranscribeAsync(
        string audioPath,
        string language,
        string modelName,
        string? initialPrompt = null,
        CancellationToken cancellationToken = default)
    {
        // 1. 确保工具已下载
        await _toolManager.EnsureToolAsync(cancellationToken);

        // 2. 获取模型文件路径
        var modelPath = await _modelResolver.GetWhisperModelPathAsync(modelName, cancellationToken);
        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"Whisper model file not found: {modelPath}");

        // 3. 构建参数：使用 -oj 强制输出 JSON，输出文件自动生成在音频同目录下
        var args = $"-m \"{modelPath}\" -f \"{audioPath}\" -l {language} -oj";
        if (!string.IsNullOrEmpty(initialPrompt))
            args += $" -p \"{initialPrompt}\"";

        _logger.LogDebug("Executing: {Exe} {Args}", _toolManager.ExecutablePath, args);

        // 4. 执行进程（输出会生成 JSON 文件，标准输出可能只是进度或日志）
        var output = await _processManager.ExecuteAsync(
            _toolManager.ExecutablePath,
            args,
            cancellationToken: cancellationToken);

        // 5. 确定 JSON 文件路径（与音频同目录同文件名，扩展名为 .json）
        var jsonPath = Path.ChangeExtension(audioPath, ".json");
        if (!File.Exists(jsonPath))
            throw new InvalidOperationException($"Whisper.cpp did not produce expected JSON file: {jsonPath}");

        // 6. 读取并解析 JSON
        var jsonContent = File.ReadAllText(jsonPath);
        var whisperResult = JsonConvert.DeserializeObject<WhisperTranscriptJson>(jsonContent)
            ?? throw new InvalidOperationException("Failed to deserialize Whisper JSON output.");

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