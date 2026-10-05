using System.Globalization;
using Centurion.Models.Workflow;

namespace Centurion.Core.Utils.Reporting;

/// <summary>
/// CI threshold rule: of the form <c>cps&gt;20</c> / <c>coverage&lt;95</c>.
/// Metric names (cps/maxcps/meancps/linelen/overlap/minms/maxms/coverage/confidence/glossary/lengthdev/ttsdev)
/// match the quality command --fail-on documentation; percentage metrics (coverage/glossary) are compared on a 0~100 scale.
/// </summary>
public sealed record QualityThresholdRule(string Metric, string Op, double Value)
{
    /// <summary>The rule's raw expression (e.g. "cps>20", used for the FailedThresholds display).</summary>
    public string Expression => $"{Metric}{Op}{Value.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Parses the expression; returns null if the format is invalid.</summary>
    public static QualityThresholdRule? TryParse(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return null;

        var trimmed = expression.Trim();
        var opIndex = trimmed.IndexOfAny(['>', '<', '=']);
        if (opIndex <= 0)
            return null;

        var metric = trimmed[..opIndex].Trim().ToLowerInvariant();
        var op = trimmed[opIndex];
        // Handle >= / <= / == / !=
        var opText = op.ToString();
        if (opIndex + 1 < trimmed.Length && (trimmed[opIndex + 1] == '=' || trimmed[opIndex + 1] == '>'))
        {
            opText += trimmed[opIndex + 1];
            opIndex++;
        }
        var rest = trimmed[(opIndex + 1)..].Trim();
        if (!double.TryParse(rest, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return null;

        return SupportedMetrics.Contains(metric) ? new QualityThresholdRule(metric, opText, value) : null;
    }

    private static readonly HashSet<string> SupportedMetrics = new(StringComparer.Ordinal)
    {
        "cps", "maxcps", "meancps", "linelen", "overlap", "minms", "maxms",
        "coverage", "confidence", "glossary", "lengthdev", "ttsdev"
    };

    /// <summary>Extracts the metric value (percentage metrics are converted to 0~100 before comparison).</summary>
    public bool Evaluate(QualityReport report)
    {
        var actual = ExtractValue(report);
        if (actual is null)
            return false; // a metric with no data is treated as not satisfied (CI should fail and flag it)

        var value = actual.Value;
        return opText switch
        {
            ">" => value > Value,
            ">=" => value >= Value,
            "<" => value < Value,
            "<=" => value <= Value,
            "==" => Math.Abs(value - Value) < 1e-9,
            "!=" => Math.Abs(value - Value) >= 1e-9,
            _ => false
        };
    }

    private readonly string opText = Op;

    /// <summary>Extracts the metric's current value; returns null when data is missing.</summary>
    private double? ExtractValue(QualityReport report)
    {
        switch (Metric)
        {
            case "cps":
            case "maxcps":
                return report.Timing?.MaxCps;
            case "meancps":
                return report.Timing?.MeanCps;
            case "linelen":
                return report.Timing?.LineTooLongCount;
            case "overlap":
                return report.Timing?.OverlapCount;
            case "minms":
                return report.Timing is null ? null : (double?)report.Alignment.MinSentenceDurationMs;
            case "maxms":
                return report.Timing is null ? null : (double?)report.Alignment.MaxSentenceDurationMs;
            case "coverage":
                return Math.Round(report.Alignment.MapperCoverage * 100.0, 2);
            case "confidence":
                return report.Confidence?.MeanConfidence;
            case "glossary":
                return report.Translation is null ? null : Math.Round(report.Translation.GlossaryHitRate * 100.0, 2);
            case "lengthdev":
                return report.Translation?.LengthDeviation;
            case "ttsdev":
                return report.Tts?.MeanAlignmentErrorMs;
            default:
                return null;
        }
    }
}
