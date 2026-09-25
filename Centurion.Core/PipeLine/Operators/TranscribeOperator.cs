using System.Globalization;
using System.Text;
using Centurion.Abstractions;
using Centurion.Abstractions.Factories;
using Centurion.Abstractions.Pipeline;
using Centurion.Abstractions.Exceptions;
using Centurion.Models;
using Centurion.Models.Workflow;
using Centurion.Core.Utils;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Pipeline.Operators;

/// <summary>
/// 转录算子，通过工厂动态选择转录策略（Whisper/Qwen/API 等）。
/// </summary>
public class TranscribeOperator(
    ITranscriptionStrategyFactory factory,
    ILogger<TranscribeOperator> logger) : PipelineOperatorBase<TranscribeOperator>(logger)
{
    private readonly ITranscriptionStrategyFactory _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    /// <summary>算子在管道中的显示名称。</summary>
    public override string Name => "Transcription";

    /// <summary>
    /// 执行转录：经工厂创建转录策略，对音频做语音识别得到词流，
    /// 清理非法字符后聚合成句并写回工作流状态。
    /// </summary>
    /// <param name="context">字幕工作流上下文，提供音频路径与转录配置。</param>
    /// <param name="cancellationToken">用于取消转录过程的取消标记。</param>
    public override async Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken)
    {
        // 检查点：若已转录则跳过
        if (context.State.IsTranscribed)
        {
            LogInfo("Transcription already exists, skipping.");
            if (context.State.TranscribeSentences.Count == 0)
            {
                const string message = "Transcription is marked complete but contains no sentences.";
                context.State.Errors.Add(message);
                throw new InvalidOperationException(message);
            }

            context.State.CurrentSentences = context.State.TranscribeSentences;
            return;
        }

        var config = context.Config;

        // 获取音频路径（人声分离后优先使用人声轨）
        var audioPath = context.State.VocalsPath
            ?? context.State.PreprocessedAudioPath
            ?? context.State.ConvertedAudioPath
            ?? config.InputFilePath;
        if (!File.Exists(audioPath))
            throw new FileNotFoundException($"Audio file not found: {audioPath}");

        // 通过工厂创建具体策略
        var strategy = _factory.Create(
            config.TranscriberEngine,
            config.TranscriberModel,
            config.Language,
            config.InitialPrompt
        );

        LogInfo($"Using transcription strategy: {strategy.StrategyName}");

        try
        {
            var words = await strategy.TranscribeAsync(
                audioPath,
                config.Language,
                config.TranscriberModel ?? "base",
                config.InitialPrompt,
                cancellationToken,
                config.Device);

            if (words == null || words.Count == 0)
                throw new Exception("Transcription returned no words.");

            // ---- 后处理：清理每个词的文本，移除非法字符（音乐符号等） ----
            var cleanedWords = new List<Word>();
            foreach (var w in words)
            {
                var cleanedText = CleanWordText(w.Text);
                if (!string.IsNullOrWhiteSpace(cleanedText))
                {
                    cleanedWords.Add(new Word
                    {
                        Text = cleanedText,
                        Start = w.Start,
                        End = w.End,
                        Speaker = w.Speaker
                    });
                }
            }

            if (cleanedWords.Count == 0)
                throw new Exception("After cleaning, no words remain.");

            // ---- 词级时间戳健康化：排序/钳制/补零时长，稳定下游分句与说话人标注 ----
            var sanitizedWords = WordTimingSanitizer.Sanitize(cleanedWords);

            // 聚合成一个句子（后续分句会拆分）；中文等无空格语系不插入空格
            var aggregatedText = Centurion.Models.Text.LanguageSupport.JoinWords(
                sanitizedWords.Select(w => w.Text), config.Language);
            var sentence = new Sentence
            {
                Text = aggregatedText,
                Start = sanitizedWords.First().Start,
                End = sanitizedWords.Last().End,
                Words = sanitizedWords
            };

            context.State.TranscribeSentences = [sentence];
            context.State.CurrentSentences = context.State.TranscribeSentences;
            context.State.IsTranscribed = true;

            LogInfo($"Transcription completed. {cleanedWords.Count} words, duration {(sentence.End - sentence.Start) / 1000.0:F2}s");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogWarning($"Transcription failed: {ex.Message}");
            throw new WhisperProcessException("Transcription failed.", -1, ex.Message);
        }
    }

    /// <summary>
    /// 清理词文本：移除音乐符号、控制字符等非文本内容。
    /// 保留字母、数字、常见标点（. , ! ? 等）和空格，保留中文字符。
    /// </summary>
    private static string CleanWordText(string input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        // 定义需要移除的特定字符（来自实际观察到的非法符号）
        // 也可使用 Unicode 类别过滤，但为简单起见，我们先移除已知的符号。
        // 注意：这些字符可能因模型不同而变化，我们采用更通用的方式。
        // 移除所有控制字符、格式字符、其他符号（除常见标点外）
        var sb = new StringBuilder();
        foreach (var c in input)
        {
            var cat = char.GetUnicodeCategory(c);
            // 保留字母、数字、空格、常见标点（. , ! ? ; : 等）和中文字符（归类为其他字母）
            if (cat == UnicodeCategory.UppercaseLetter ||
                cat == UnicodeCategory.LowercaseLetter ||
                cat == UnicodeCategory.TitlecaseLetter ||
                cat == UnicodeCategory.DecimalDigitNumber ||
                cat == UnicodeCategory.OtherLetter ||      // 中文字符
                cat == UnicodeCategory.SpaceSeparator ||
                cat == UnicodeCategory.OtherPunctuation || // 标点如 . , ! ?
                cat == UnicodeCategory.DashPunctuation ||  // 连字符
                cat == UnicodeCategory.InitialQuotePunctuation ||
                cat == UnicodeCategory.FinalQuotePunctuation)
            {
                sb.Append(c);
            }
            // 其他类别（如 MathSymbol, CurrencySymbol, ModifierSymbol, OtherSymbol）一概丢弃
        }

        return sb.ToString().Trim();
    }
}
