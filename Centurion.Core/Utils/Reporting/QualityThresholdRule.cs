using System.Globalization;
using Centurion.Models.Workflow;

namespace Centurion.Core.Utils.Reporting;

/// <summary>
/// CI 阈值规则：形如 <c>cps&gt;20</c> / <c>coverage&lt;95</c>。
/// 指标名（cps/maxcps/meancps/linelen/overlap/minms/maxms/coverage/confidence/glossary/lengthdev/ttsdev）
/// 与 quality 命令 --fail-on 文档一致；百分数指标（coverage/glossary）按 0~100 比较。
/// </summary>
public sealed record QualityThresholdRule(string Metric, string Op, double Value)
{
    /// <summary>规则的原始表达式（如 "cps>20"，用于 FailedThresholds 展示）。</summary>
    public string Expression => $"{Metric}{Op}{Value.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>解析表达式；格式非法返回 null。</summary>
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
        // 处理 >= / <= / == / !=
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

    /// <summary>提取指标数值（百分数指标转换为 0~100 后比较）。</summary>
    public bool Evaluate(QualityReport report)
    {
        var actual = ExtractValue(report);
        if (actual is null)
            return false; // 指标无数据视为不满足（CI 应失败并提示）

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

    /// <summary>提取指标当前值；数据缺失（null）返回 null。</summary>
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
