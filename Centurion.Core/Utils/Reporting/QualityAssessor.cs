using Centurion.Models;
using Centurion.Models.Workflow;

namespace Centurion.Core.Utils.Reporting;

/// <summary>行级质量评估的阈值选项。</summary>
public class QualityAssessmentOptions
{
    /// <summary>允许的最大语速（字符/秒）；默认 5.0（保守可读性标准）。</summary>
    public double MaxCps { get; init; } = 5.0;

    /// <summary>单行字幕最大字符数；默认 18。</summary>
    public int MaxCharsPerLine { get; init; } = 18;

    /// <summary>最小时长阈值（毫秒）；低于此值视为过短可合并。默认 300ms。</summary>
    public double MinSentenceDurationMs { get; init; } = 300;

    /// <summary>最大时长阈值（毫秒）；超过此值视为过长需拆分。默认 7000ms。</summary>
    public double MaxSentenceDurationMs { get; init; } = 7000;

    /// <summary>ASR 置信度阈值（0~1）；句子置信度低于此值标 LowConfidence。默认 0.5。</summary>
    public double ConfidenceThreshold { get; init; } = 0.5;

    /// <summary>重叠判定后保留的最小句间间隙（毫秒）。默认 50ms。</summary>
    public double MinGapMs { get; init; } = 50;
}

/// <summary>
/// 行级质量评估器：对字幕句子计算 CPS / 行宽 / 最小时长 / 最大时长 / 重叠 / 零时长 /
/// 置信度等指标，并生成可逐行定位的问题明细（含建议修复），供 .quality.json、
/// HTML 报告与 quality --fix 共用。
/// </summary>
public static class QualityAssessor
{
    /// <summary>评估结果：时序统计 + 行级问题。</summary>
    public sealed record Assessment(QualityTiming Timing, List<QualityLineIssue> Issues, List<QualityLineIssue> LowConfidenceFragments);

    /// <summary>
    /// 对句子列表执行行级质量评估。
    /// </summary>
    /// <param name="sentences">字幕句子（Start/End 毫秒）。</param>
    /// <param name="options">阈值选项。</param>
    /// <param name="confidenceMap">句级置信度（0~1），可空。</param>
    /// <returns>时序统计与行级问题列表。</returns>
    public static Assessment Assess(
        List<Sentence> sentences,
        QualityAssessmentOptions options,
        IReadOnlyDictionary<int, double>? confidenceMap = null)
    {
        var issues = new List<QualityLineIssue>();
        var cpsValues = new List<double>();
        var gaps = new List<double>();

        for (var i = 0; i < sentences.Count; i++)
        {
            var sentence = sentences[i];
            var durationMs = sentence.End - sentence.Start;
            var text = string.IsNullOrWhiteSpace(sentence.TranslatedText) ? sentence.Text : sentence.TranslatedText;
            var charCount = text?.Count(ch => !char.IsWhiteSpace(ch)) ?? 0;

            // 零时长
            if (durationMs <= 0)
            {
                issues.Add(MakeIssue(QualityIssueType.ZeroDuration, QualityIssueSeverity.Error, i, sentence,
                    $"Zero-duration sentence ({durationMs:F0}ms).",
                    "Assign a non-zero duration (quality --fix adjusts timeline).",
                    durationMs, 0));
            }

            // CPS
            if (durationMs > 0)
            {
                var cps = charCount / (durationMs / 1000.0);
                cpsValues.Add(cps);
                if (cps > options.MaxCps)
                {
                    issues.Add(MakeIssue(QualityIssueType.CpsTooHigh, QualityIssueSeverity.Error, i, sentence,
                        $"CPS {cps:F1} exceeds limit {options.MaxCps:F1}.",
                        "Split the sentence or extend its duration (quality --fix splits long lines).",
                        Math.Round(cps, 2), options.MaxCps));
                }
            }

            // 行宽
            if (charCount > options.MaxCharsPerLine)
            {
                issues.Add(MakeIssue(QualityIssueType.LineTooLong, QualityIssueSeverity.Warning, i, sentence,
                    $"{charCount} characters exceed per-line limit {options.MaxCharsPerLine}.",
                    "Insert a line break or split the sentence (quality --fix inserts CJK line breaks).",
                    charCount, options.MaxCharsPerLine));
            }

            // 最小时长 / 最大时长
            if (durationMs > 0 && durationMs < options.MinSentenceDurationMs)
            {
                issues.Add(MakeIssue(QualityIssueType.TooShort, QualityIssueSeverity.Warning, i, sentence,
                    $"Duration {durationMs:F0}ms is below minimum {options.MinSentenceDurationMs:F0}ms.",
                    "Merge with an adjacent sentence (quality --fix merges short lines).",
                    Math.Round(durationMs, 1), options.MinSentenceDurationMs));
            }

            if (durationMs > options.MaxSentenceDurationMs)
            {
                issues.Add(MakeIssue(QualityIssueType.TooLong, QualityIssueSeverity.Warning, i, sentence,
                    $"Duration {durationMs / 1000.0:F1}s exceeds maximum {options.MaxSentenceDurationMs / 1000.0:F1}s.",
                    "Split into shorter lines (quality --fix splits long lines).",
                    Math.Round(durationMs, 1), options.MaxSentenceDurationMs));
            }

            // 与下一句重叠 / 句间间隙
            if (i + 1 < sentences.Count)
            {
                var gapMs = sentences[i + 1].Start - sentence.End;
                gaps.Add(gapMs / 1000.0);
                if (gapMs < -options.MinGapMs)
                {
                    issues.Add(MakeIssue(QualityIssueType.Overlap, QualityIssueSeverity.Error, i, sentence,
                        $"Overlaps next line by {-gapMs:F0}ms.",
                        "Adjust start/end so lines do not overlap (quality --fix fixes timeline).",
                        Math.Round(-gapMs, 1), options.MinGapMs));
                }
            }
        }

        var timing = new QualityTiming
        {
            MeanCps = cpsValues.Count > 0 ? Math.Round(cpsValues.Average(), 2) : 0,
            MaxCps = cpsValues.Count > 0 ? Math.Round(cpsValues.Max(), 2) : 0,
            CpsTooHighCount = issues.Count(i => i.Type == nameof(QualityIssueType.CpsTooHigh)),
            LineTooLongCount = issues.Count(i => i.Type == nameof(QualityIssueType.LineTooLong)),
            TooShortCount = issues.Count(i => i.Type == nameof(QualityIssueType.TooShort)),
            TooLongCount = issues.Count(i => i.Type == nameof(QualityIssueType.TooLong)),
            OverlapCount = issues.Count(i => i.Type == nameof(QualityIssueType.Overlap)),
            TotalOverlapSeconds = Math.Round(
                issues.Where(i => i.Type == nameof(QualityIssueType.Overlap)).Sum(i => (i.Value ?? 0) / 1000.0), 3),
            MeanGapSeconds = gaps.Count > 0 ? Math.Round(gaps.Average(), 3) : 0
        };

        // 低置信度片段
        var lowConfidence = new List<QualityLineIssue>();
        var lowIndexes = new List<int>();
        if (confidenceMap is not null)
        {
            foreach (var (index, confidence) in confidenceMap)
            {
                if (index >= 0 && index < sentences.Count && confidence < options.ConfidenceThreshold)
                {
                    lowIndexes.Add(index);
                    lowConfidence.Add(MakeIssue(QualityIssueType.LowConfidence, QualityIssueSeverity.Warning, index, sentences[index],
                        $"ASR confidence {confidence:F2} below threshold {options.ConfidenceThreshold:F2}.",
                        "Re-transcribe the fragment or review manually.",
                        Math.Round(confidence, 3), options.ConfidenceThreshold));
                }
            }
        }

        issues.AddRange(lowConfidence);
        return new Assessment(timing, issues, lowConfidence);
    }

    private static QualityLineIssue MakeIssue(
        QualityIssueType type, QualityIssueSeverity severity, int index, Sentence sentence,
        string message, string fix, double? value, double? limit)
        => new()
        {
            Type = type.ToString(),
            Severity = severity.ToString(),
            SentenceIndex = index,
            StartMs = Math.Round(sentence.Start, 1),
            EndMs = Math.Round(sentence.End, 1),
            Text = sentence.TranslatedText ?? sentence.Text ?? string.Empty,
            Message = message,
            Fix = fix,
            Value = value,
            Limit = limit
        };
}
