using Centurion.Models;
using Centurion.Models.Workflow;

namespace Centurion.Core.Utils.Reporting;

/// <summary>Threshold options for line-level quality assessment.</summary>
public class QualityAssessmentOptions
{
    /// <summary>Maximum allowed speech rate (characters/second); defaults to 5.0 (conservative readability standard).</summary>
    public double MaxCps { get; init; } = 5.0;

    /// <summary>Maximum characters per subtitle line; defaults to 18.</summary>
    public int MaxCharsPerLine { get; init; } = 18;

    /// <summary>Minimum duration threshold (milliseconds); below this a line is treated as too short and mergeable. Defaults to 300ms.</summary>
    public double MinSentenceDurationMs { get; init; } = 300;

    /// <summary>Maximum duration threshold (milliseconds); above this a line is treated as too long and needs splitting. Defaults to 7000ms.</summary>
    public double MaxSentenceDurationMs { get; init; } = 7000;

    /// <summary>ASR confidence threshold (0~1); sentences below this are flagged LowConfidence. Defaults to 0.5.</summary>
    public double ConfidenceThreshold { get; init; } = 0.5;

    /// <summary>Minimum inter-line gap kept after overlap detection (milliseconds). Defaults to 50ms.</summary>
    public double MinGapMs { get; init; } = 50;
}

/// <summary>
/// Line-level quality assessor: computes CPS / line width / minimum duration / maximum duration /
/// overlap / zero-duration / confidence and other metrics for subtitle sentences, and produces
/// per-line locatable issue details (with suggested fixes), shared by .quality.json, the HTML
/// report, and quality --fix.
/// </summary>
public static class QualityAssessor
{
    /// <summary>Assessment result: timing statistics + line-level issues.</summary>
    public sealed record Assessment(QualityTiming Timing, List<QualityLineIssue> Issues, List<QualityLineIssue> LowConfidenceFragments);

    /// <summary>
    /// Runs line-level quality assessment on a list of sentences.
    /// </summary>
    /// <param name="sentences">Subtitle sentences (Start/End in milliseconds).</param>
    /// <param name="options">Threshold options.</param>
    /// <param name="confidenceMap">Per-sentence confidence (0~1), may be null.</param>
    /// <returns>Timing statistics and the list of line-level issues.</returns>
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

            // Zero duration
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

            // Line width
            if (charCount > options.MaxCharsPerLine)
            {
                issues.Add(MakeIssue(QualityIssueType.LineTooLong, QualityIssueSeverity.Warning, i, sentence,
                    $"{charCount} characters exceed per-line limit {options.MaxCharsPerLine}.",
                    "Insert a line break or split the sentence (quality --fix inserts CJK line breaks).",
                    charCount, options.MaxCharsPerLine));
            }

            // Minimum / maximum duration
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

            // Overlap with the next line / inter-line gap
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

        // Low-confidence fragments
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
