using System.Text;
using Centurion.Abstractions.Pipeline;
using Spectre.Console;

namespace Centurion.Cli.Commands;

/// <summary>Renders a DAG pipeline as Mermaid / text topology / HTML (for the pipeline graph command).</summary>
internal static class PipelineGraphRenderer
{
    /// <summary>Renders a Mermaid flowchart (nodes carry condition/retry/degrade labels; dependencies solid, conditional nodes dashed).</summary>
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

    /// <summary>Renders an indented text topology (console-friendly).</summary>
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

    /// <summary>Renders a Spectre Tree (modern console topology: nodes carry condition/retry/degrade labels; dependencies as tree levels).</summary>
    public static Tree RenderTree(PipelineDag dag)
    {
        var tree = new Tree("[bold cyan]DAG Topology[/]");
        var visited = new HashSet<string>(StringComparer.Ordinal);

        void AddChildren(TreeNode? parent, string name)
        {
            if (!visited.Add(name))
                return;
            var node = dag.Find(name)!;
            var label = new StringBuilder(Markup.Escape(node.Name));
            var ann = new List<string>();
            if (node.When is not null) ann.Add("[gold]when[/]");
            if (node.MaxRetries > 0) ann.Add($"[dim]retry x{node.MaxRetries}[/]");
            if (node.DegradeOnFailure) ann.Add("[dim]degrade[/]");
            if (node.Timeout is not null) ann.Add($"[dim]timeout {node.Timeout.Value.TotalSeconds:F0}s[/]");
            if (node.Description is not null) ann.Add($"[grey]{Markup.Escape(node.Description)}[/]");
            var labelText = ann.Count > 0
                ? $"{label} [dim]({string.Join(", ", ann)})[/]"
                : label.ToString();

            var child = parent is null
                ? tree.AddNode(labelText)
                : parent.AddNode(labelText);
            foreach (var childNode in dag.Nodes.Where(n => n.DependsOn.Contains(name, StringComparer.Ordinal)))
                AddChildren(child, childNode.Name);
        }

        foreach (var node in dag.Nodes.Where(n => n.DependsOn.Count == 0))
            AddChildren(null, node.Name);
        // 保护：环或孤立节点仍全量展示
        foreach (var node in dag.Nodes)
        {
            if (!visited.Contains(node.Name))
                AddChildren(null, node.Name);
        }
        return tree;
    }

    /// <summary>Renders a self-contained HTML page (embeds the Mermaid.js CDN; degrades to a text list offline).</summary>
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
        "<p>Mermaid CDN unreachable — degraded to a text topology (below).</p>");
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
