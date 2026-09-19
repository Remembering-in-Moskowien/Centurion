using Centurion.Abstractions.Factories;
using Centurion.Abstractions.Pipeline;
using Centurion.Abstractions.Strategy;
using Centurion.Models;
using Centurion.Models.Workflow;
using Centurion.Core.Strategy.Diarization;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Pipeline.Operators;

/// <summary>
/// 说话人分割算子：对当前工作集的每个 Word 按时间中点匹配说话人片段并标注 Speaker。
/// 后端由 WorkflowConfig.DiarizationBackend 选择（"crispasr" / "pyannote" / "none"）。
/// 非致命错误仅记录警告，不中断管道。
/// </summary>
public sealed class DiarizationOperator(
    IDiarizationStrategyFactory factory,
    ILogger<DiarizationOperator> logger) : PipelineOperatorBase<DiarizationOperator>(logger)
{
    private readonly IDiarizationStrategyFactory _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    /// <summary>算子在管道中的显示名称。</summary>
    public override string Name => "Speaker Diarization";

    /// <summary>
    /// 执行说话人分割：按配置创建策略，对音频做说话人分离，
    /// 并把每个词按时间中点标注对应说话人。失败为非致命错误，仅记录警告。
    /// </summary>
    /// <param name="context">字幕工作流上下文，提供音频、句子与配置。</param>
    /// <param name="cancellationToken">用于取消说话人分割过程的取消标记。</param>
    public override async Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken)
    {
        var config = context.Config;

        // 1. 后端开关
        if (string.Equals(config.DiarizationBackend, "none", StringComparison.OrdinalIgnoreCase))
        {
            LogInfo("Diarization disabled (DiarizationBackend = none).");
            return;
        }

        // 2. 音频路径（优先人声轨，其次预处理后的音频）
        var audioPath = context.State.VocalsPath
            ?? context.State.PreprocessedAudioPath
            ?? context.State.ConvertedAudioPath
            ?? config.InputFilePath;
        if (string.IsNullOrEmpty(audioPath) || !File.Exists(audioPath))
        {
            LogWarning($"Audio file unavailable for diarization: {audioPath}");
            return;
        }

        // 3. 待标注句子（当前工作集，回退到转录结果）
        var sentences = context.State.CurrentSentences.Count > 0
            ? context.State.CurrentSentences
            : context.State.TranscribeSentences;
        if (sentences.Count == 0)
        {
            LogInfo("No sentences to annotate; skipping diarization.");
            return;
        }

        // 4. 创建策略
        IDiarizationStrategy strategy;
        try
        {
            strategy = _factory.Create(config.DiarizationBackend);
        }
        catch (NotSupportedException ex)
        {
            LogWarning(ex.Message);
            return;
        }

        // 5. crispasr 后端允许通过配置覆盖内置方法（energy/xcorr/vad-turns/foxnose）
        if (strategy is CrispAsrDiarizationStrategy crispStrategy
            && !string.IsNullOrWhiteSpace(config.DiarizationMethod)
            && !string.Equals(config.DiarizationMethod, "pyannote", StringComparison.OrdinalIgnoreCase))
        {
            crispStrategy.Method = config.DiarizationMethod;
        }

        try
        {
            OnProgress(10, "Running speaker diarization");
            var turns = await strategy.DiarizeAsync(
                audioPath, config.NumSpeakers, config.DiarizationModel, cancellationToken, config.Device);

            if (turns.Count == 0)
            {
                LogWarning("Diarization returned no speaker segments.");
                return;
            }

            // 6. 按时间中点把说话人映射到每个 Word（Word 为引用类型，原地标注）
            var annotated = sentences.ToList();
            var annotatedWordCount = 0;
            foreach (var sentence in annotated)
            {
                foreach (var word in sentence.Words)
                {
                    word.Speaker = ResolveSpeaker(word, turns);
                    annotatedWordCount++;
                }
            }

            context.State.DiarizedSentences = annotated;
            context.State.IsDiarized = true;
            OnProgress(100, "Diarization completed");
            LogInfo($"Assigned speakers to {annotatedWordCount} words across {turns.Count} segments.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 非致命：说话人标注失败不中断字幕生成
            LogWarning($"Speaker diarization failed; continuing without speaker labels. {ex.Message}");
        }
    }

    /// <summary>
    /// 按 Word 时间中点匹配说话人片段（internal，便于单元测试）。
    /// 未命中时回退到默认说话人标签。
    /// </summary>
    internal static string ResolveSpeaker(Word word, IReadOnlyList<SpeakerSegment> turns)
    {
        if (word == null || turns == null || turns.Count == 0)
            return "SPEAKER_00";

        var midMs = (word.Start + word.End) / 2.0;
        foreach (var turn in turns)
        {
            var startMs = turn.StartSeconds * 1000.0;
            var endMs = turn.EndSeconds * 1000.0;
            if (midMs >= startMs && midMs <= endMs)
                return turn.Speaker;
        }
        return "SPEAKER_00";
    }
}
