using System.Text;
using System.Web;
using Centurion.Models.Workflow;

namespace Centurion.Core.Utils.Reporting;

/// <summary>
/// 把 <see cref="QualityReport"/> 渲染为自包含 HTML 质量报告：
/// 概览、指标卡、阈值结果与可逐行定位的问题表（行锚点 id="line-N"，N 为字幕行号）。
/// 无外部依赖，双击即可打开。
/// </summary>
public static class QualityHtmlReport
{
    /// <summary>渲染完整 HTML 报告。</summary>
    public static string Render(QualityReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"zh-CN\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"utf-8\"/>");
        sb.AppendLine("<title>Centurion 质量报告</title>");
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
        sb.AppendLine("<h1>Centurion 质量报告</h1>");
        sb.AppendLine($"<div class=\"meta\">命令 <b>{Esc(report.Meta.Command)}</b> · {Esc(report.Meta.InputFile)} → {Esc(report.Meta.OutputFile)} · 生成于 {report.Meta.GeneratedAt:yyyy-MM-dd HH:mm:ss} · 耗时 {report.Meta.ElapsedSeconds:F1}s</div>");
        sb.AppendLine($"<div style=\"margin-top:10px;\"><span class=\"badge {(report.Passed ? "pass" : "fail")}\">{(report.Passed ? "PASS 阈值全部通过" : "FAIL 质量不达标")}</span>");
        if (report.FailedThresholds.Count > 0)
            sb.AppendLine($" <span class=\"badge fail\">{Esc(string.Join(" · ", report.FailedThresholds))}</span>");
        sb.AppendLine("</div></header>");

        // Counts + Coverage
        sb.AppendLine("<section><h2 class=\"sec\">规模与覆盖</h2><div class=\"cards\">");
        Card(sb, "句子", report.Counts.SentenceCount.ToString(), $"{report.Counts.WordCount} 词 / {report.Counts.CharacterCount} 字符");
        Card(sb, "时长", $"{report.Counts.DurationSeconds:F1}s", $"平均语速 {report.Counts.CharactersPerSecond:F1} 字符/s");
        Card(sb, "说话人", report.Counts.SpeakerCount.ToString(), report.Coverage.Diarized ? "已分割" : "未分割");
        var coverageParts = new List<string>();
        if (report.Coverage.Transcribed) coverageParts.Add("转录");
        if (report.Coverage.Split) coverageParts.Add("分句");
        if (report.Coverage.Aligned) coverageParts.Add("对齐");
        if (report.Coverage.Translated) coverageParts.Add("翻译");
        Card(sb, "阶段", coverageParts.Count > 0 ? string.Join(" · ", coverageParts) : "无", $"映射覆盖率 {(report.Alignment.MapperCoverage * 100):F1}%");
        sb.AppendLine("</div></section>");

        // Timing
        if (report.Timing is not null)
        {
            sb.AppendLine("<section><h2 class=\"sec\">时序与可读性</h2><div class=\"cards\">");
            Card(sb, "平均语速", $"{report.Timing.MeanCps:F2} CPS", $"超限行 {report.Timing.CpsTooHighCount}");
            Card(sb, "最大语速", $"{report.Timing.MaxCps:F2} CPS", "阈值 5.0");
            Card(sb, "超行宽行数", report.Timing.LineTooLongCount.ToString(), "阈值 18 字符/行");
            Card(sb, "过短/过长", $"{report.Timing.TooShortCount} / {report.Timing.TooLongCount}", "300ms–7000ms");
            Card(sb, "重叠", $"{report.Timing.OverlapCount} 处", $"{report.Timing.TotalOverlapSeconds:F2}s 总量 · 平均间隙 {report.Timing.MeanGapSeconds:F2}s");
            sb.AppendLine("</div></section>");
        }

        // Confidence / Translation / Tts
        if (report.Confidence is { MeanConfidence: not null })
            sb.AppendLine(Section(
                "ASR 置信度",
                $"<div class=\"kv\"><div><dt>平均置信度</dt><dd>{report.Confidence.MeanConfidence:F3}</dd></div>" +
                $"<div><dt>低置信度句子（&lt;0.5）</dt><dd>{report.Confidence.LowConfidenceSentenceIndexes.Count}</dd></div></div>"));
        if (report.Translation is not null)
            sb.AppendLine(Section(
                "翻译 QA",
                $"<div class=\"kv\"><div><dt>术语命中率</dt><dd>{report.Translation.GlossaryHitRate * 100:F1}% ({report.Translation.GlossaryHits}/{report.Translation.GlossaryExpected})</dd></div>" +
                $"<div><dt>长度比均值</dt><dd>{report.Translation.MeanLengthRatio:F2}</dd></div>" +
                $"<div><dt>长度偏差</dt><dd>{report.Translation.LengthDeviation:F2}</dd></div>" +
                (report.Translation.BackTranslateSimilarity is { } bts
                    ? $"<div><dt>回译相似度</dt><dd>{bts:F3}</dd></div>"
                    : "<div><dt>回译相似度</dt><dd class=\"warn\">未启用</dd></div>") +
                $"<div><dt>缓存命中</dt><dd>{report.Translation.CachedSentenceCount}</dd></div></div>"));
        if (report.Tts is not null)
            sb.AppendLine(Section(
                "TTS / 配音",
                $"<div class=\"kv\"><div><dt>平均对齐误差</dt><dd>{report.Tts.MeanAlignmentErrorMs:F0}ms</dd></div>" +
                $"<div><dt>最大对齐误差</dt><dd>{report.Tts.MaxAlignmentErrorMs:F0}ms</dd></div>" +
                $"<div><dt>语速</dt><dd>{report.Tts.MeanSpeechRate:F2} 字符/s</dd></div>" +
                $"<div><dt>句间停顿</dt><dd>均值 {report.Tts.MeanPauseSeconds:F2}s / 最大 {report.Tts.MaxPauseSeconds:F2}s</dd></div>" +
                $"<div><dt>负间隙（重叠）</dt><dd>{report.Tts.NegativeGapSeconds:F2}s</dd></div>" +
                $"<div><dt>Ducking</dt><dd>{(report.Tts.DuckingApplied ? "已应用" : "未应用")}</dd></div>" +
                (report.Tts.LoudnessLufs is { } lufs
                    ? $"<div><dt>响度</dt><dd>{lufs:F1} LUFS</dd></div>"
                    : "<div><dt>响度</dt><dd class=\"warn\">未探测</dd></div>") +
                "</div>"));

        // Issues table
        sb.AppendLine("<section><h2 class=\"sec\">行级问题</h2>");
        if (report.Issues.Count == 0)
        {
            sb.AppendLine("<p style=\"color:#047857;font-weight:600;\">未发现行级问题。</p>");
        }
        else
        {
            sb.AppendLine("<table><thead><tr><th style=\"width:70px;\">行号</th><th>类型</th><th style=\"width:150px;\">时间轴</th><th>文本</th><th>问题 / 建议修复</th></tr></thead><tbody>");
            foreach (var issue in report.Issues.OrderBy(i => i.SentenceIndex))
            {
                var rowClass = issue.Severity == QualityIssueSeverity.Error.ToString() ? "issue-error" : "issue-warning";
                var lineNo = issue.SentenceIndex + 1;
                sb.AppendLine($"<tr id=\"line-{lineNo}\" class=\"{rowClass}\">");
                sb.AppendLine($"<td><a class=\"rowlink\" href=\"#line-{lineNo}\">#{lineNo}</a></td>");
                sb.AppendLine($"<td><span class=\"type {Esc(issue.Type)}\">{Esc(issue.Type)}</span></td>");
                sb.AppendLine($"<td class=\"mono\">{issue.StartMs:F0} → {issue.EndMs:F0} ms</td>");
                sb.AppendLine($"<td>{Esc(Truncate(issue.Text, 80))}</td>");
                sb.AppendLine($"<td>{Esc(issue.Message)}<br/><span class=\"fix\">修复：{Esc(issue.Fix ?? "手动处理")}</span></td>");
                sb.AppendLine("</tr>");
            }
            sb.AppendLine("</tbody></table>");
        }
        sb.AppendLine("</section>");

        // Warnings / Errors
        if (report.Warnings.Count > 0)
            sb.AppendLine(Section("警告", "<ul>" + string.Concat(report.Warnings.Select(w => $"<li class=\"warn\">{Esc(w)}</li>")) + "</ul>"));
        if (report.Errors.Count > 0)
            sb.AppendLine(Section("错误", "<ul>" + string.Concat(report.Errors.Select(e => $"<li class=\"err\">{Esc(e)}</li>")) + "</ul>"));

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
