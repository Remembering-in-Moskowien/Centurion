using Centurion.Core.Abstractions;
using Centurion.Core.Abstractions.Factories;
using Centurion.Core.Exceptions;
using Centurion.Core.Models;

namespace Centurion.Core.PipeLine;

/// <summary>
/// 转录算子，通过工厂动态选择转录策略（Whisper/Qwen/API 等）。
/// </summary>
public class TranscribeOperator : PipelineOperatorBase
{
    private readonly ITranscriptionStrategyFactory _factory;

    public override string Name => "Transcription";

    public TranscribeOperator(ITranscriptionStrategyFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public override async Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken)
    {
        // 检查点：若已转录则跳过
        if (context.State.IsTranscribed)
        {
            LogInfo("Transcription already exists, skipping.");
            return;
        }

        var config = context.Config;

        // 获取音频路径
        var audioPath = context.State.ConvertedAudioPath ?? config.InputFilePath;
        if (!File.Exists(audioPath))
            throw new FileNotFoundException($"Audio file not found: {audioPath}");

        // 通过工厂创建具体策略
        var strategy = _factory.Create(
            config.TranscriberEngine,    // 如 "whisper", "qwen", "api"
            config.TranscriberModel,     // 如 "base", "large", "qwen-asr-1.0"
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
                cancellationToken);

            if (words == null || words.Count == 0)
                throw new Exception("Transcription returned no words.");

            // 聚合成一个句子（后续分句会拆分）
            var aggregatedText = string.Join(" ", words.Select(w => w.Text));
            var sentence = new Sentence
            {
                Text = aggregatedText,
                Start = words.First().Start,
                End = words.Last().End,
                Words = words
            };

            context.State.WhisperSentences = new List<Sentence> { sentence };
            context.State.IsTranscribed = true;

            LogInfo($"Transcription completed. {words.Count} words, duration {sentence.End - sentence.Start:F2}s");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogError($"Transcription failed: {ex.Message}");
            throw new WhisperProcessException("Transcription failed.", -1, ex.Message);
        }
    }
}