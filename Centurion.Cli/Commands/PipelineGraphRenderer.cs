using System.Text;
using Centurion.Abstractions.Pipeline;

namespace Centurion.Cli.Commands;

/// <summary>把 DAG 管线渲染为 Mermaid / 文本拓扑 / HTML（供 pipeline graph 命令输出）。</summary>
internal static class PipelineGraphRenderer
{
    /// <summary>渲染为 Mermaid flowchart（节点含条件/重试/降级标注，依赖为实线，条件节点虚线）。</summary>
    public static string RenderMermaid(PipelineDag dag)
    {
        var sb = new StringBuilder();
        sb.AppendLine("flowchart LR");
        var index = 0;
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var node in dag.Nodes)
        {
            var id = $"N{index++}";
            names[node.Name] = id;

            var label = new StringBuilder(MermaidEscape(node.Name));
            var annotations = new List<string>();
            if (node.When is not null) annotations.Add("when: condition");
            if (node.MaxRetries > 0) annotations.Add($"retry x{node.MaxRetries}");
            if (node.Timeout is not null) annotations.Add($"timeout {node.Timeout.Value.TotalSeconds:F0}s");
            if (node.DegradeOnFailure) annotations.Add("degrade");
            if (node.Description is not null) annotations.Add(MermaidEscape(node.Description));
            if (annotations.Count > 0)
                label.Append("<br/>").Append(string.Join(" · ", annotations));

            sb.AppendLine($"  {id}[\"{label}\"]");
        }

        foreach (var node in dag.Nodes)
        {
            foreach (var dependency in node.DependsOn)
            {
                if (!names.TryGetValue(dependency, out var from))
                    continue;
                var arrow = node.When is not null ? "-. conditional .->" : "-->";
                sb.AppendLine($"  {from} {arrow} {names[node.Name]}");
            }
        }

        return sb.ToString();
    }

    /// <summary>渲染为缩进文本拓扑（控制台友好）。</summary>
    public static string RenderText(PipelineDag dag)
    {
        var sb = new StringBuilder();
        var names = dag.Nodes.Select(n => n.Name).ToHashSet(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);

        void Visit(string name, int depth)
        {
            if (!visited.Add(name))
                return;
            var node = dag.Find(name)!;
            sb.Append(' ', depth * 2);
            sb.Append("- ").Append(node.Name);
            if (node.When is not null) sb.Append(" [when: condition]");
            if (node.MaxRetries > 0) sb.Append($" [retry x{node.MaxRetries}]");
            if (node.DegradeOnFailure) sb.Append(" [degrade]");
            if (node.Timeout is not null) sb.Append($" [timeout {node.Timeout.Value.TotalSeconds:F0}s]");
            if (node.Description is not null) sb.Append(" — ").Append(node.Description);
            sb.AppendLine();
            foreach (var child in dag.Nodes.Where(n => n.DependsOn.Contains(name, StringComparer.Ordinal)))
                Visit(child.Name, depth + 1);
        }

        foreach (var node in dag.Nodes.Where(n => n.DependsOn.Count == 0))
            Visit(node.Name, 0);

        // 保护：环或孤立节点仍全量展示
        foreach (var node in dag.Nodes)
        {
            if (!visited.Contains(node.Name))
                Visit(node.Name, 0);
        }

        return sb.ToString();
    }

    /// <summary>渲染为自包含 HTML（内嵌 Mermaid.js CDN；离线时退化为文本列表）。</summary>
    public static string RenderHtml(PipelineDag dag)
    {
        var mermaid = RenderMermaid(dag);
        var text = RenderText(dag);
        var safeText = System.Net.WebUtility.HtmlEncode(text).Replace("\n", "<br/>");
        return $$"""
<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="utf-8"/>
<title>Centurion Pipeline DAG</title>
<style>
  body { font-family: system-ui, "Microsoft YaHei", sans-serif; margin: 24px; color: #222; }
  h1 { font-size: 20px; }
  h2 { font-size: 15px; margin-top: 28px; }
  pre { background: #f6f8fa; padding: 12px; border-radius: 8px; overflow: auto; font-size: 13px; line-height: 1.6; }
</style>
</head>
<body>
<h1>Centurion Pipeline DAG</h1>
<div class="mermaid">{{mermaid}}</div>
<h2>拓扑摘要</h2>
<pre>{{safeText}}</pre>
<script type="module">
  import mermaid from "https://cdn.jsdelivr.net/npm/mermaid@11/dist/mermaid.esm.min.mjs";
  mermaid.initialize({ startOnLoad: true, theme: "base" });
  window.addEventListener("load", () => {
    try { mermaid.run(); } catch (e) {
      document.querySelector(".mermaid")?.remove();
      document.body.insertAdjacentHTML("beforeend",
        "<p>Mermaid CDN 不可达，已退化为文本拓扑（见下）。</p>");
    }
  });
</script>
</body>
</html>
""";
    }

    private static string MermaidEscape(string value) =>
        value.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;");
}
