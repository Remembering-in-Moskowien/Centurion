using Centurion.Models;
using Centurion.Models.Workflow;

namespace Centurion.Core.Utils.Reporting;

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

        // 行级质量评估（CPS / 行宽 / 最小时长 / 最大时长 / 重叠 / 置信度）→ Timing + Issues
        var assessment = QualityAssessor.Assess(sentences, new QualityAssessmentOptions(), BuildConfidenceMap(sentences));
        report.Timing = assessment.Timing;
        report.Issues = assessment.Issues;

        // ASR 置信度（模型提供时）
        report.Confidence = BuildConfidence(sentences);
        if (report.Confidence.MeanConfidence is null && sentences.Count > 0)
            report.Warnings.Add("ASR model did not provide per-sentence confidence; LowConfidence analysis is skipped.");

        // 翻译 QA（TranslationOperator 已写入 TranslationQa）
        if (state.TranslationQa is not null)
            report.Translation = MapTranslationQa(state.TranslationQa);
        else if (state.IsTranslated && sentences.Count > 0)
            report.Warnings.Add("Translation QA is unavailable (no glossary/length metrics recorded for translated sentences).");

        // TTS / 配音（dub 命令时）
        if (string.Equals(context.Config.CommandName, "dub", StringComparison.OrdinalIgnoreCase))
            report.Tts = BuildTts(context, sentences);

        return report;
    }

    /// <summary>句级置信度映射（index → 0~1；仅供 Assess 的低置信度判定）。</summary>
    private static IReadOnlyDictionary<int, double> BuildConfidenceMap(List<Sentence> sentences)
    {
        var map = new Dictionary<int, double>();
        for (var i = 0; i < sentences.Count; i++)
        {
            if (sentences[i].Confidence is { } c)
                map[i] = c;
        }
        return map;
    }

    /// <summary>ASR 置信度聚合：平均 + 低置信度索引（阈值 0.5）。</summary>
    private static QualityConfidence BuildConfidence(List<Sentence> sentences)
    {
        var values = sentences.Select(s => s.Confidence).Where(c => c is not null).Select(c => c!.Value).ToList();
        var confidence = new QualityConfidence
        {
            MeanConfidence = values.Count > 0 ? Math.Round(values.Average(), 3) : null,
            LowConfidenceSentenceIndexes = sentences
                .Select((s, i) => (s, i))
                .Where(x => x.s.Confidence is { } c && c < 0.5)
                .Select(x => x.i)
                .ToList()
        };
        return confidence;
    }

    /// <summary>TranslationQa → 报告质量翻译指标（回译相似度未启用，恒为 null）。</summary>
    private static QualityTranslation MapTranslationQa(TranslationQa qa) => new()
    {
        GlossaryHitRate = qa.GlossaryHitRate,
        GlossaryHits = qa.GlossaryHits,
        GlossaryExpected = qa.GlossaryExpected,
        MeanLengthRatio = qa.MeanLengthRatio,
        LengthDeviation = qa.LengthDeviation,
        CachedSentenceCount = qa.CachedSentenceCount
    };

    /// <summary>dub 命令的 TTS 指标：对齐误差、语速、停顿、重叠、ducking（响度探测未接入时警告）。</summary>
    private static QualityTts BuildTts(SubtitleWorkflowContext context, List<Sentence> sentences)
    {
        var segments = context.State.DubSegments;
        var succeeded = segments.Where(s => !s.Skipped).ToList();
        var errors = succeeded
            .Where(s => s.TargetDurationSec > 0 && s.AlignedDurationSec > 0)
            .Select(s => Math.Abs(s.TargetDurationSec - s.AlignedDurationSec) * 1000.0)
            .ToList();
        var gaps = new List<double>();
        var negativeGapSeconds = 0.0;
        var ordered = succeeded.OrderBy(s => s.TargetStartMs).ToList();
        for (var i = 0; i + 1 < ordered.Count; i++)
        {
            var gap = (ordered[i + 1].TargetStartMs - ordered[i].TargetEndMs) / 1000.0;
            gaps.Add(gap);
            if (gap < 0)
                negativeGapSeconds += -gap;
        }

        var tts = new QualityTts
        {
            MeanAlignmentErrorMs = errors.Count > 0 ? Math.Round(errors.Average(), 1) : 0,
            MaxAlignmentErrorMs = errors.Count > 0 ? Math.Round(errors.Max(), 1) : 0,
            MeanSpeechRate = ComputeSpeechRate(sentences),
            MeanPauseSeconds = gaps.Count > 0 ? Math.Round(gaps.Where(g => g > 0).DefaultIfEmpty(0).Average(), 3) : 0,
            MaxPauseSeconds = gaps.Count > 0 ? Math.Round(gaps.Max(), 3) : 0,
            NegativeGapSeconds = Math.Round(negativeGapSeconds, 3),
            DuckingApplied = context.Config.DubDucking && !string.IsNullOrWhiteSpace(context.Config.DubBackgroundPath)
        };
        return tts;
    }

    /// <summary>TTS 合成段平均语速（字符/秒；无段时 0）。</summary>
    private static double ComputeSpeechRate(List<Sentence> sentences)
    {
        var segments = sentences
            .Where(s => s.End > s.Start)
            .Select(s => (Chars: s.Text?.Count(ch => !char.IsWhiteSpace(ch)) ?? 0, Seconds: (s.End - s.Start) / 1000.0))
            .Where(x => x.Seconds > 0)
            .ToList();
        return segments.Count == 0
            ? 0
            : Math.Round(segments.Average(x => x.Chars / x.Seconds), 2);
    }

    /// <summary>构建 dub 专属指标（非 dub 命令返回 null）。</summary>
    private static QualityDub? BuildDub(SubtitleWorkflowContext context, List<Sentence> sentences)
    {
        if (!string.Equals(context.Config.CommandName, "dub", StringComparison.OrdinalIgnoreCase))
            return null;

        var segments = context.State.DubSegments;

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
    public static List<Sentence> EffectiveSentences(WorkflowState state)
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
