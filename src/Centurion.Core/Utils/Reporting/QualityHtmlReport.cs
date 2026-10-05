using System.Text;
using System.Web;
using Centurion.Models.Workflow;

namespace Centurion.Core.Utils.Reporting;

/// <summary>
/// Renders <see cref="QualityReport"/> into a self-contained HTML quality report:
/// overview, metric cards, threshold results, and a per-line locatable issue table
/// (row anchor id="line-N", N being the subtitle line number).
/// No external dependencies; just double-click to open.
/// </summary>
public static class QualityHtmlReport
{
    /// <summary>Renders the full HTML report.</summary>
    public static string Render(QualityReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"zh-CN\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"utf-8\"/>");
        sb.AppendLine("<title>Centurion Quality Report</title>");
        sb.AppendLine("""
<style>
  body { font-family: system-ui, "Microsoft YaHei", sans-serif; margin: 0; background: #f5f6f8; color: #1f2328; }
  .wrap { max-width: 1080px; margin: 0 auto; padding: 24px 16px 48px; }
  header { background: #fff; border: 1px solid #e5e7eb; border-radius: 12px; padding: 20px 24px; margin-bottom: 16px; }
  h1 { font-size: 20px; margin: 0 0 4px; }
  .meta { color: #6b7280; font-size: 13px; }
  .badge { display: inline-block; padding: 3px 12px; border-radius: 999px; font-size: 13px; font-weight: 600; }
  .badge.pass { background: #ecfdf5; color: #047857; border: 1px solid #a7f3d0; }
  .badge.fail { background: #fef2f2; color: #b91c1c; border: 1px solid #fecaca; }
  .cards { display: flex; flex-wrap: wrap; gap: 12px; margin: 16px 0; }
  .card { background: #fff; border: 1px solid #e5e7eb; border-radius: 12px; padding: 14px 18px; flex: 1 1 200px; min-width: 180px; }
  .card h2 { font-size: 13px; color: #6b7280; margin: 0 0 8px; font-weight: 600; }
  .card .big { font-size: 22px; font-weight: 700; }
  .card .sub { font-size: 12px; color: #6b7280; margin-top: 4px; }
  section { background: #fff; border: 1px solid #e5e7eb; border-radius: 12px; padding: 18px 24px; margin-bottom: 16px; }
  h2.sec { font-size: 16px; margin: 0 0 12px; }
  table { width: 100%; border-collapse: collapse; font-size: 13px; }
  th, td { text-align: left; padding: 8px 10px; border-bottom: 1px solid #f0f1f3; vertical-align: top; }
  th { color: #6b7280; font-weight: 600; background: #fafbfc; }
  tr.issue-error { background: #fff7f7; }
  tr.issue-warning { background: #fffdf5; }
  .type { display: inline-block; padding: 1px 8px; border-radius: 6px; font-size: 12px; font-weight: 600; white-space: nowrap; }
  .type.CpsTooHigh, .type.Overlap, .type.ZeroDuration { background: #fef2f2; color: #b91c1c; }
  .type.LineTooLong, .type.TooShort, .type.TooLong, .type.LowConfidence { background: #fffbeb; color: #b45309; }
  .type.GlossaryMiss, .type.LengthDeviation { background: #eff6ff; color: #1d4ed8; }
  .fix { color: #047857; }
  .mono { font-family: Consolas, monospace; font-size: 12px; }
  .warn { color: #b45309; }
  .err { color: #b91c1c; }
  .rowlink { color: #1d4ed8; text-decoration: none; }
  .rowlink:hover { text-decoration: underline; }
  .kv { display: grid; grid-template-columns: 1fr 1fr; gap: 4px 24px; font-size: 13px; }
  .kv div { display: flex; justify-content: space-between; border-bottom: 1px dashed #f0f1f3; padding: 4px 0; }
  .kv dt { color: #6b7280; } .kv dd { margin: 0; font-weight: 600; }
</style>
""");
        sb.AppendLine("</head>");
        sb.AppendLine("<body><div class=\"wrap\">");

        // Header
        sb.AppendLine("<header>");
        sb.AppendLine("<h1>Centurion Quality Report</h1>");
        sb.AppendLine($"<div class=\"meta\">Command <b>{Esc(report.Meta.Command)}</b> · {Esc(report.Meta.InputFile)} → {Esc(report.Meta.OutputFile)} · Generated at {report.Meta.GeneratedAt:yyyy-MM-dd HH:mm:ss} · Elapsed {report.Meta.ElapsedSeconds:F1}s</div>");
        sb.AppendLine($"<div style=\"margin-top:10px;\"><span class=\"badge {(report.Passed ? "pass" : "fail")}\">{(report.Passed ? "PASS all thresholds passed" : "FAIL quality below threshold")}</span>");
        if (report.FailedThresholds.Count > 0)
            sb.AppendLine($" <span class=\"badge fail\">{Esc(string.Join(" · ", report.FailedThresholds))}</span>");
        sb.AppendLine("</div></header>");

        // Counts + Coverage
        sb.AppendLine("<section><h2 class=\"sec\">Scale & Coverage</h2><div class=\"cards\">");
        Card(sb, "Sentences", report.Counts.SentenceCount.ToString(), $"{report.Counts.WordCount} words / {report.Counts.CharacterCount} chars");
        Card(sb, "Duration", $"{report.Counts.DurationSeconds:F1}s", $"avg rate {report.Counts.CharactersPerSecond:F1} chars/s");
        Card(sb, "Speakers", report.Counts.SpeakerCount.ToString(), report.Coverage.Diarized ? "Diarized" : "Not diarized");
        var coverageParts = new List<string>();
        if (report.Coverage.Transcribed) coverageParts.Add("Transcribe");
        if (report.Coverage.Split) coverageParts.Add("Split");
        if (report.Coverage.Aligned) coverageParts.Add("Align");
        if (report.Coverage.Translated) coverageParts.Add("Translate");
        Card(sb, "Stages", coverageParts.Count > 0 ? string.Join(" · ", coverageParts) : "None", $"Mapper coverage {(report.Alignment.MapperCoverage * 100):F1}%");
        sb.AppendLine("</div></section>");

        // Timing
        if (report.Timing is not null)
        {
            sb.AppendLine("<section><h2 class=\"sec\">Timing & Readability</h2><div class=\"cards\">");
            Card(sb, "Mean speech rate", $"{report.Timing.MeanCps:F2} CPS", $"over-limit lines {report.Timing.CpsTooHighCount}");
            Card(sb, "Max speech rate", $"{report.Timing.MaxCps:F2} CPS", "threshold 5.0");
            Card(sb, "Lines over width", report.Timing.LineTooLongCount.ToString(), "threshold 18 chars/line");
            Card(sb, "Too short/long", $"{report.Timing.TooShortCount} / {report.Timing.TooLongCount}", "300ms–7000ms");
            Card(sb, "Overlap", $"{report.Timing.OverlapCount} overlaps", $"{report.Timing.TotalOverlapSeconds:F2}s total · avg gap {report.Timing.MeanGapSeconds:F2}s");
            sb.AppendLine("</div></section>");
        }

        // Confidence / Translation / Tts
        if (report.Confidence is { MeanConfidence: not null })
            sb.AppendLine(Section(
                "ASR Confidence",
                $"<div class=\"kv\"><div><dt>Mean confidence</dt><dd>{report.Confidence.MeanConfidence:F3}</dd></div>" +
                $"<div><dt>Low-confidence sentences (&lt;0.5)</dt><dd>{report.Confidence.LowConfidenceSentenceIndexes.Count}</dd></div></div>"));
        if (report.Translation is not null)
            sb.AppendLine(Section(
                "Translation QA",
                $"<div class=\"kv\"><div><dt>Glossary hit rate</dt><dd>{report.Translation.GlossaryHitRate * 100:F1}% ({report.Translation.GlossaryHits}/{report.Translation.GlossaryExpected})</dd></div>" +
                $"<div><dt>Mean length ratio</dt><dd>{report.Translation.MeanLengthRatio:F2}</dd></div>" +
                $"<div><dt>Length deviation</dt><dd>{report.Translation.LengthDeviation:F2}</dd></div>" +
                (report.Translation.BackTranslateSimilarity is { } bts
                    ? $"<div><dt>Back-translation similarity</dt><dd>{bts:F3}</dd></div>"
                    : "<div><dt>Back-translation similarity</dt><dd class=\"warn\">Disabled</dd></div>") +
                $"<div><dt>Cache hits</dt><dd>{report.Translation.CachedSentenceCount}</dd></div></div>"));
        if (report.Tts is not null)
            sb.AppendLine(Section(
                "TTS / Dubbing",
                $"<div class=\"kv\"><div><dt>Mean alignment error</dt><dd>{report.Tts.MeanAlignmentErrorMs:F0}ms</dd></div>" +
                $"<div><dt>Max alignment error</dt><dd>{report.Tts.MaxAlignmentErrorMs:F0}ms</dd></div>" +
                $"<div><dt>Speech rate</dt><dd>{report.Tts.MeanSpeechRate:F2} chars/s</dd></div>" +
                $"<div><dt>Inter-sentence pause</dt><dd>mean {report.Tts.MeanPauseSeconds:F2}s / max {report.Tts.MaxPauseSeconds:F2}s</dd></div>" +
                $"<div><dt>Negative gap (overlap)</dt><dd>{report.Tts.NegativeGapSeconds:F2}s</dd></div>" +
                $"<div><dt>Ducking</dt><dd>{(report.Tts.DuckingApplied ? "Applied" : "Not applied")}</dd></div>" +
                (report.Tts.LoudnessLufs is { } lufs
                    ? $"<div><dt>Loudness</dt><dd>{lufs:F1} LUFS</dd></div>"
                    : "<div><dt>Loudness</dt><dd class=\"warn\">Not measured</dd></div>") +
                "</div>"));

        // Issues table
        sb.AppendLine("<section><h2 class=\"sec\">Line-level issues</h2>");
        if (report.Issues.Count == 0)
        {
            sb.AppendLine("<p style=\"color:#047857;font-weight:600;\">No line-level issues found.</p>");
        }
        else
        {
            sb.AppendLine("<table><thead><tr><th style=\"width:70px;\">Line</th><th>Type</th><th style=\"width:150px;\">Timeline</th><th>Text</th><th>Issue / Suggested fix</th></tr></thead><tbody>");
            foreach (var issue in report.Issues.OrderBy(i => i.SentenceIndex))
            {
                var rowClass = issue.Severity == QualityIssueSeverity.Error.ToString() ? "issue-error" : "issue-warning";
                var lineNo = issue.SentenceIndex + 1;
                sb.AppendLine($"<tr id=\"line-{lineNo}\" class=\"{rowClass}\">");
                sb.AppendLine($"<td><a class=\"rowlink\" href=\"#line-{lineNo}\">#{lineNo}</a></td>");
                sb.AppendLine($"<td><span class=\"type {Esc(issue.Type)}\">{Esc(issue.Type)}</span></td>");
                sb.AppendLine($"<td class=\"mono\">{issue.StartMs:F0} → {issue.EndMs:F0} ms</td>");
                sb.AppendLine($"<td>{Esc(Truncate(issue.Text, 80))}</td>");
                sb.AppendLine($"<td>{Esc(issue.Message)}<br/><span class=\"fix\">Fix: {Esc(issue.Fix ?? "Manual handling")}</span></td>");
                sb.AppendLine("</tr>");
            }
            sb.AppendLine("</tbody></table>");
        }
        sb.AppendLine("</section>");

        // Warnings / Errors
        if (report.Warnings.Count > 0)
            sb.AppendLine(Section("Warnings", "<ul>" + string.Concat(report.Warnings.Select(w => $"<li class=\"warn\">{Esc(w)}</li>")) + "</ul>"));
        if (report.Errors.Count > 0)
            sb.AppendLine(Section("Errors", "<ul>" + string.Concat(report.Errors.Select(e => $"<li class=\"err\">{Esc(e)}</li>")) + "</ul>"));

        sb.AppendLine("</div></body></html>");
        return sb.ToString();
    }

    private static void Card(StringBuilder sb, string title, string big, string sub)
    {
        sb.AppendLine($"<div class=\"card\"><h2>{Esc(title)}</h2><div class=\"big\">{Esc(big)}</div><div class=\"sub\">{Esc(sub)}</div></div>");
    }

    private static string Section(string title, string body)
        => $"<section><h2 class=\"sec\">{Esc(title)}</h2>{body}</section>";

    private static string Esc(string? value) => HttpUtility.HtmlEncode(value ?? string.Empty);

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max] + "…";
}
