using Centurion.Models;
using Centurion.Models.Workflow;

namespace Centurion.Core.Utils;

/// <summary>
/// 从 <see cref="SubtitleWorkflowContext"/> 提取指标并构建 <see cref="QualityReport"/>。
/// 统计逻辑独立于 IO，便于单元测试；所有路径（spawn/from-script/correct/convert/translate/dub）统一使用。
/// </summary>
public static class QualityReportBuilder
{
    /// <summary>
    /// 根据工作流上下文构建质量报告。
    /// </summary>
    /// <param name="context">字幕工作流上下文。</param>
    /// <param name="outputPath">本次运行的输出文件路径（用于 Meta）。</param>
    /// <param name="elapsedSeconds">本次运行总耗时（秒）。</param>
    /// <returns>质量报告。</returns>
    public static QualityReport Build(SubtitleWorkflowContext context, string outputPath, double elapsedSeconds)
    {
        var state = context.State;
        var sentences = EffectiveSentences(state);

        var words = sentences.SelectMany(s => s.Words ?? []).ToList();
        var characters = sentences.Sum(s => s.Text?.Count(ch => !char.IsWhiteSpace(ch)) ?? 0);

        var report = new QualityReport
        {
            Meta = new QualityMeta
            {
                Command = context.Config.CommandName,
                GeneratedAt = DateTimeOffset.Now,
                InputFile = context.Config.InputFilePath,
                OutputFile = outputPath,
                ElapsedSeconds = elapsedSeconds
            },
            Counts = new QualityCounts
            {
                SentenceCount = sentences.Count,
                WordCount = words.Count,
                CharacterCount = characters,
                SpeakerCount = DistinctSpeakerCount(sentences),
                DurationSeconds = ComputeDurationSeconds(sentences),
                CharactersPerSecond = ComputeCps(sentences)
            },
            Coverage = new QualityCoverage
            {
                Transcribed = state.IsTranscribed,
                Diarized = state.IsDiarized,
                Split = state.IsSplit,
                Aligned = state.IsAligned,
                Translated = state.IsTranslated,
                TranscribedSentenceCount = state.TranscribeSentences.Count,
                AlignedSentenceCount = state.AlignedSentences.Count,
                TranslatedSentenceCount = state.TranslatedSentences?.Count ?? 0
            },
            Alignment = BuildAlignment(state, sentences),
            Dub = BuildDub(context, sentences),
            Warnings = state.Warnings.ToList(),
            Errors = state.Errors.ToList()
        };

        // 校准命令自带漂移统计：优先直接取用
        if (state.Report is { AverageDriftMs: > 0 } correction)
        {
            report.Alignment.MeanDriftMs = correction.AverageDriftMs;
            report.Alignment.MaxDriftMs = correction.MaxDriftMs;
            report.Alignment.MapperCoverage = correction.TextCoverage;
        }

        return report;
    }

    /// <summary>构建 dub 专属指标（非 dub 命令返回 null）。</summary>
    private static QualityDub? BuildDub(SubtitleWorkflowContext context, List<Sentence> sentences)
    {
        if (!string.Equals(context.Config.CommandName, "dub", StringComparison.OrdinalIgnoreCase))
            return null;

        var segments = context.State.Extensions.TryGetValue("DubSegments", out var raw)
            ? raw as List<DubSegment> ?? []
            : [];

        var succeeded = segments.Where(s => !s.Skipped).ToList();
        // 配音文本覆盖率：目标语言文本（译文轨或单轨原文）非空的句子占比
        var translatedCount = sentences.Count(s => !string.IsNullOrWhiteSpace(s.TranslatedText ?? s.Text));
        var deviations = succeeded
            .Where(s => s.TargetDurationSec > 0 && s.AlignedDurationSec > 0)
            .Select(s => Math.Abs(s.TargetDurationSec - s.AlignedDurationSec) * 1000.0)
            .ToList();
        var tempos = succeeded.Where(s => s.AlignmentTempo > 0).Select(s => s.AlignmentTempo).ToList();

        // 克隆一致性启发式：合成时长越贴近目标时长，节奏一致性越高（0~1）；非真实声纹相似度
        double consistency = 1.0;
        if (deviations.Count > 0)
        {
            var meanDeviationRatio = deviations.Average() / 1000.0 /
                Math.Max(0.1, succeeded.Where(s => s.TargetDurationSec > 0).Select(s => s.TargetDurationSec).DefaultIfEmpty(1).Average());
            consistency = Math.Round(Math.Clamp(1.0 - meanDeviationRatio, 0.0, 1.0), 3);
        }

        return new QualityDub
        {
            SegmentsTotal = segments.Count,
            SegmentsSucceeded = succeeded.Count,
            SegmentsSkipped = segments.Count - succeeded.Count,
            TranslationCoverage = sentences.Count > 0 ? Math.Round(translatedCount / (double)sentences.Count, 3) : 0,
            MeanAlignmentDeviationMs = deviations.Count > 0 ? Math.Round(deviations.Average(), 1) : 0,
            MaxAlignmentDeviationMs = deviations.Count > 0 ? Math.Round(deviations.Max(), 1) : 0,
            TempoMin = tempos.Count > 0 ? Math.Round(tempos.Min(), 3) : 1,
            TempoMax = tempos.Count > 0 ? Math.Round(tempos.Max(), 3) : 1,
            TempoAverage = tempos.Count > 0 ? Math.Round(tempos.Average(), 3) : 1,
            CloneConsistency = consistency
        };
    }

    /// <summary>取当前工作集；为空时回退到最新有内容的阶段列表（对齐/分割/转录）。</summary>
    private static List<Sentence> EffectiveSentences(WorkflowState state)
    {
        if (state.CurrentSentences.Count > 0)
            return state.CurrentSentences;

        if (state.AlignedSentences.Count > 0)
            return state.AlignedSentences;

        if (state.DiarizedSentences.Count > 0)
            return state.DiarizedSentences;

        if (state.SplitSentences.Count > 0)
            return state.SplitSentences;

        if (state.TranscribeSentences.Count > 0)
            return state.TranscribeSentences;

        return state.SubtitleSentences.Count > 0 ? state.SubtitleSentences : [];
    }

    private static int DistinctSpeakerCount(List<Sentence> sentences)
    {
        var speakers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sentence in sentences)
        {
            if (!string.IsNullOrWhiteSpace(sentence.Speaker))
                speakers.Add(sentence.Speaker);
            else if (sentence.Words is not null)
            {
                foreach (var word in sentence.Words)
                {
                    if (!string.IsNullOrWhiteSpace(word.Speaker))
                        speakers.Add(word.Speaker);
                }
            }
        }
        return speakers.Count;
    }

    private static double ComputeDurationSeconds(List<Sentence> sentences)
    {
        if (sentences.Count == 0)
            return 0;
        var minStart = sentences.Select(s => s.Start).Min();
        var maxEnd = sentences.Select(s => s.End).Max();
        // Sentence.Start/End 全项目统一为毫秒
        return Math.Max(0, (maxEnd - minStart) / 1000.0);
    }

    private static double ComputeCps(List<Sentence> sentences)
    {
        if (sentences.Count == 0)
            return 0;
        var chars = sentences.Sum(s => s.Text?.Count(ch => !char.IsWhiteSpace(ch)) ?? 0);
        var seconds = ComputeDurationSeconds(sentences);
        return seconds > 0 ? Math.Round(chars / seconds, 2) : 0;
    }

    private static QualityAlignment BuildAlignment(WorkflowState state, List<Sentence> sentences)
    {
        var durations = sentences
            .Select(s => (s.End - s.Start) * 1000)
            .Where(ms => ms >= 0)
            .ToList();

        var zeroCount = sentences.Count(s => s.End == s.Start && s.Start != 0);

        return new QualityAlignment
        {
            MeanDriftMs = state.Report.AverageDriftMs > 0 ? state.Report.AverageDriftMs : null,
            MaxDriftMs = state.Report.MaxDriftMs > 0 ? state.Report.MaxDriftMs : null,
            MapperCoverage = state.MapperCoverage,
            ZeroDurationSentenceCount = zeroCount,
            MinSentenceDurationMs = durations.Count > 0 ? durations.Min() : 0,
            MaxSentenceDurationMs = durations.Count > 0 ? durations.Max() : 0
        };
    }
}
