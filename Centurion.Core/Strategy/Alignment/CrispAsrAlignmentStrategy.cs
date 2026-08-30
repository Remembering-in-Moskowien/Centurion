// 文件: Centurion.Strategies.Alignment/CrispAsrAlignmentStrategy.cs

using Centurion.Core.Abstractions;
using Centurion.Core.Abstractions.Strategy;
using Centurion.Core.Managers;
using Centurion.Core.Models;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Strategy.Alignment;

/// <summary>
/// 基于 CrispASR 外部工具的强制对齐策略。
/// 通过调用 crispasr.exe --align-only 实现词级时间戳对齐。
/// </summary>
public class CrispAsrAlignmentStrategy(
    IModelPathResolver modelPathResolver,
    IServiceProvider serviceProvider,
    ILogger<CrispAsrAlignmentStrategy> logger,
    string modelName = "wav2vec2-base-960h")
    : IAlignmentStrategy
{
    // CrispASR 对齐输出格式: 每行一个词，包含开始时间、结束时间和词本身
    private const string AlignFormat = "words";

    public async Task<List<Sentence>> AlignAsync(
        List<Sentence> sentences,
        string audioPath,
        CancellationToken cancellationToken)
    {
        if (sentences.Count == 0)
            return sentences;

        // 1. 确保 CrispASR 工具已安装
        var toolManager = new ToolManager("crispasr", serviceProvider);
        await toolManager.EnsureToolAsync(cancellationToken);

        // 2. 获取对齐模型路径
        var modelPath = await modelPathResolver.GetQwen3ForcedAlignerPathAsync(modelName, cancellationToken);

        // 3. 拼接完整文本（用于 --ref-text）
        var fullText = string.Join(" ", sentences.Select(s => s.Text));

        // 4. 创建临时输出文件
        var tempOutput = Path.GetTempFileName();

        try
        {
            // 5. 构建命令行参数
            // 格式: crispasr --align-only -am <model> -f <audio> --ref-text "<text>" --align-format words --align-output <output>
            var arguments = $"--align-only " +
                            $"-am \"{modelPath}\" " +
                            $"-f \"{audioPath}\" " +
                            $"--ref-text \"{fullText}\" " +
                            $"--align-format {AlignFormat} " +
                            $"--align-output \"{tempOutput}\"";

            logger.LogDebug("执行 CrispASR: {Executable} {Arguments}", toolManager.ExecutablePath, arguments);

            // 6. 执行进程
            var processManager = new ProcessManager(logger);
            _ = await processManager.ExecuteAsync(
                toolManager.ExecutablePath,
                arguments,
                timeoutMs: 300000, // 5 分钟超时
                cancellationToken);

            // 7. 解析输出并更新句子
            var wordTimings = ParseAlignOutput(tempOutput);
            MapTimingsToWords(sentences, wordTimings);

            return sentences;
        }
        finally
        {
            // 清理临时文件
            if (File.Exists(tempOutput))
                File.Delete(tempOutput);
        }
    }

    /// <summary>
    /// 解析 CrispASR 的 --align-format words 输出。
    /// 输出格式: 每行一个词，制表符分隔: start_time\tend_time\tword
    /// </summary>
    private List<(string word, double start, double end)> ParseAlignOutput(string outputPath)
    {
        var results = new List<(string, double, double)>();

        if (!File.Exists(outputPath))
            return results;

        var lines = File.ReadAllLines(outputPath);
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var parts = line.Split('\t');
            if (parts.Length >= 3 &&
                double.TryParse(parts[0], out var start) &&
                double.TryParse(parts[1], out var end))
            {
                results.Add((parts[2].Trim(), start, end));
            }
        }

        return results;
    }

    /// <summary>
    /// 将词级时间戳映射到 Sentence.Words 中。
    /// </summary>
    private void MapTimingsToWords(List<Sentence> sentences, List<(string word, double start, double end)> wordTimings)
    {
        var wordIdx = 0;

        foreach (var sentence in sentences)
        {
            var words = sentence.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            foreach (var w in words)
            {
                if (wordIdx >= wordTimings.Count)
                    break;

                var timing = wordTimings[wordIdx];

                // 查找已存在的 Word（可能已有说话人信息）
                var existingWord = sentence.Words.FirstOrDefault(x => x.Text == w);
                if (existingWord != null)
                {
                    existingWord.Start = timing.start;
                    existingWord.End = timing.end;
                }
                else
                {
                    sentence.Words.Add(new Word
                    {
                        Text = w,
                        Start = timing.start,
                        End = timing.end,
                        Speaker = sentence.Words.FirstOrDefault()?.Speaker ?? "UNKNOWN"
                    });
                }

                wordIdx++;
            }

            // 更新句子的起止时间
            if (sentence.Words.Any())
            {
                sentence.Start = sentence.Words.Min(w => w.Start);
                sentence.End = sentence.Words.Max(w => w.End);
            }
        }
    }
}