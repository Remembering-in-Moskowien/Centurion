using Centurion.Abstractions.Pipeline;
using Centurion.Abstractions.Strategy;
using Centurion.Models;
using Centurion.Models.Workflow;
using Centurion.Core.Workflow.Strategy.Diarization;using Microsoft.Extensions.Logging;
using Centurion.Core.Utils.Parsing;
namespace Centurion.Core.Workflow.Pipeline.Operators;

/// <summary>
/// 说话人分割算子：对当前工作集的每个 Word 按时间中点匹配说话人片段并标注 Speaker。
/// 后端由 WorkflowConfig.DiarizationBackend 选择（"crispasr" / "pyannote" / "none"）。
/// 非致命错误仅记录警告，不中断管道。
/// </summary>
public sealed class DiarizationOperator(
    IDiarizationStrategy strategy,
    ILogger<DiarizationOperator> logger) : PipelineOperatorBase<DiarizationOperator>(logger)
{
    private readonly IDiarizationStrategy _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));

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

        // 音频路径（优先人声轨，其次预处理后的音频）
        var audioPath = context.State.VocalsPath
            ?? context.State.PreprocessedAudioPath
            ?? context.State.ConvertedAudioPath
            ?? config.InputFilePath;
        if (string.IsNullOrEmpty(audioPath) || !File.Exists(audioPath))
        {
            LogWarning($"Audio file unavailable for diarization: {audioPath}");
            return;
        }

        // 待标注句子（当前工作集，回退到转录结果）
        var sentences = context.State.CurrentSentences.Count > 0
            ? context.State.CurrentSentences
            : context.State.TranscribeSentences;
        if (sentences.Count == 0)
        {
            LogInfo("No sentences to annotate; skipping diarization.");
            return;
        }

        try
        {
            OnProgress(10, "Running speaker diarization");
            var turns = await _strategy.DiarizeAsync(
                audioPath, config.NumSpeakers, config.DiarizationModel, cancellationToken, config.Device);

            if (turns.Count == 0)
            {
                LogWarning("Diarization returned no speaker segments.");
                return;
            }

            // 6. 后处理平滑：合并相邻同说话人、消除逐段交替抖动与过短碎片，
            //    显著降低边界词的错标率
            var smoothed = SpeakerSegmentSmoother.Smooth(turns, config.DiarizationMinSegmentSeconds);
            if (smoothed.Count < turns.Count)
                LogInfo($"Speaker segments smoothed from {turns.Count} to {smoothed.Count} (min duration {config.DiarizationMinSegmentSeconds}s).");

            // 7. 按时间窗重叠最大化把说话人映射到每个 Word（Word 为引用类型，原地标注）
            var annotated = sentences.ToList();
            var annotatedWordCount = 0;
            foreach (var sentence in annotated)
            {
                foreach (var word in sentence.Words)
                {
                    word.Speaker = ResolveSpeaker(word, smoothed);
                    annotatedWordCount++;
                }
            }

            context.State.DiarizedSentences = annotated;
            context.State.IsDiarized = true;
            OnProgress(100, "Diarization completed");
            LogInfo($"Assigned speakers to {annotatedWordCount} words across {smoothed.Count} segments.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 非致命：说话人标注失败不中断字幕生成
            LogWarning($"Speaker diarization failed; continuing without speaker labels. {ex.Message}");
        }
    }

    /// <summary>
    /// 按 Word 时间窗与说话人片段的重叠量归属说话人（internal，便于单元测试）。
    /// 取与词时间窗交集最长的片段；词横跨说话人切换点时归给重叠更大的一侧，
    /// 避免时间中点法在边界处整词错标。完全无重叠（词级时间戳病态）时回退到
    /// 时间距离最近的片段；完全无片段时回退默认标签。
    /// </summary>
    internal static string ResolveSpeaker(Word word, IReadOnlyList<SpeakerSegment> turns)
    {
        if (word == null || turns == null || turns.Count == 0)
            return "SPEAKER_00";

        var bestOverlap = 0.0;
        SpeakerSegment? best = null;
        foreach (var turn in turns)
        {
            var startMs = turn.StartSeconds * 1000.0;
            var endMs = turn.EndSeconds * 1000.0;
            var overlap = Math.Min(word.End, endMs) - Math.Max(word.Start, startMs);
            if (overlap > bestOverlap)
            {
                bestOverlap = overlap;
                best = turn;
            }
        }

        if (best is not null)
            return best.Speaker;

        // 时间窗与所有片段均无重叠（词级时间戳病态或片段间隙）：
        // 取时间距离最近的说话人片段，避免大量回退到占位标签造成说话人信息丢失
        SpeakerSegment? nearest = null;
        var nearestDistance = double.MaxValue;
        foreach (var turn in turns)
        {
            var startMs = turn.StartSeconds * 1000.0;
            var endMs = turn.EndSeconds * 1000.0;
            var distance = word.End < startMs ? startMs - word.End : word.Start - endMs;
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = turn;
            }
        }

        return nearest?.Speaker ?? "SPEAKER_00";
    }
}
