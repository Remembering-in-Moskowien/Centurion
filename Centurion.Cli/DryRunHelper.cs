using Centurion.Abstractions.Pipeline;
using Centurion.Abstractions;
using Centurion.Abstractions.Providers;
using Centurion.Cli.Commands;
using Centurion.Models.Console;
using Centurion.Models.Metadata;
using Centurion.Models.Providers;
using Centurion.Models.Workflow;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

namespace Centurion.Cli;

/// <summary>
/// <c>--dry-run</c> 预览：构建完成后输出将执行的 DAG 拓扑、涉及模型（本地就绪状态）、
/// 相关 Provider 单价与 profile 取向，不执行任何算子。JSON 模式输出同构结构供脚本消费。
/// </summary>
public static class DryRunHelper
{
    /// <summary>输出 dry-run 预览（控制台树形 + 表格，或 --json 单行 JSON）。</summary>
    public static async Task<int> PreviewAsync(
        PipelineDag dag,
        WorkflowConfig config,
        IServiceProvider sp,
        bool json,
        CancellationToken ct)
    {
        var registry = sp.GetRequiredService<ModelRegistry>();
        var domains = ModelCatalog.Domains(registry);

        // 相关模型域：按节点名语义推断
        var relevantDomains = RelevantDomains(dag, domains);
        var models = new List<object>();
        foreach (var domain in relevantDomains)
        {
            foreach (var (name, meta) in domain.Models.OrderBy(m => m.Key, StringComparer.OrdinalIgnoreCase))
            {
                var ready = ModelCatalog.ExistsLocally(ModelCatalog.CreateManager(sp, domain, name));
                models.Add(new { domain = domain.Name, model = name, ready });
            }
        }

        // 相关 Provider 单价（按接口族推断）
        var providerRegistry = sp.GetRequiredService<IProviderRegistry>();
        var families = RelevantFamilies(dag);
        var providers = providerRegistry.All
            .Where(p => families.Contains(FriendlyFamily(p), StringComparer.OrdinalIgnoreCase))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => new
            {
                name = p.Name,
                family = FriendlyFamily(p),
                kind = p.Capabilities.Kind.ToString(),
                costPer1MTokensUsd = p.Capabilities.CostPer1MTokensUsd,
                costPerAudioMinuteUsd = p.Capabilities.CostPerAudioMinuteUsd,
                latency = p.Capabilities.Latency.ToString(),
                quality = p.Capabilities.Quality.ToString()
            })
            .ToList();

        var profileName = Centurion.Core.Providers.ProviderProfileResolver.Current.ToString();

        if (json)
        {
            JsonOutput.Write(new
            {
                command = config.CommandName,
                status = "dry-run",
                profile = profileName,
                nodes = dag.Nodes.Select(n => new
                {
                    name = n.Name,
                    when = n.When is not null,
                    retries = n.MaxRetries,
                    degrade = n.DegradeOnFailure,
                    timeoutSeconds = n.Timeout?.TotalSeconds
                }),
                models,
                providers,
                note = "cost is per-unit; total depends on actual media duration / token usage"
            });
            return ExitCodes.Success;
        }

        ConsoleServices.Output.WriteMarkupLine($"[bold cyan]▶ Dry-run[/] 命令 [bold]{config.CommandName}[/] · profile=[yellow]{profileName}[/]");
        AnsiConsole.Write(PipelineGraphRenderer.RenderTree(dag));

        if (models.Count > 0)
        {
            var table = new Table()
                .Border(TableBorder.Rounded)
                .Title("[bold]涉及模型[/]")
                .AddColumn(new TableColumn("域").LeftAligned())
                .AddColumn(new TableColumn("模型").LeftAligned())
                .AddColumn(new TableColumn("状态").LeftAligned());
            foreach (var m in models.Cast<dynamic>())
                table.AddRow($"[dim]{m.domain}[/]", $"[bold]{m.model}[/]",
                    m.ready ? "[green]● ready[/]" : "[red]○ missing[/]");
            AnsiConsole.Write(table);
        }

        if (providers.Count > 0)
        {
            var ptable = new Table()
                .Border(TableBorder.Rounded)
                .Title("[bold]相关 Provider（单价，总价按实际时长/token 计）[/]")
                .AddColumn(new TableColumn("接口族").LeftAligned())
                .AddColumn(new TableColumn("Provider").LeftAligned())
                .AddColumn(new TableColumn("类型").Centered())
                .AddColumn(new TableColumn("$/1M tok").RightAligned())
                .AddColumn(new TableColumn("$/min").RightAligned())
                .AddColumn(new TableColumn("质量").Centered());
            foreach (var p in providers.Cast<dynamic>())
                ptable.AddRow($"[dim]{p.family}[/]", $"[bold]{p.name}[/]",
                    p.kind == "Cloud" ? "[cyan]C[/]" : "[dim]L[/]",
                    $"{p.costPer1MTokensUsd:F2}", $"{p.costPerAudioMinuteUsd:F4}", $"{p.quality}");
            AnsiConsole.Write(ptable);
        }

        ConsoleServices.Output.WriteInfo("Dry-run: 未执行任何算子。实际运行去掉 --dry-run 即可。");
        await Task.CompletedTask;
        return ExitCodes.Success;
    }

    /// <summary>按节点名推断相关模型域（子串匹配，含大小写不敏感）。</summary>
    private static IReadOnlyList<ModelCatalog.ModelDomain> RelevantDomains(
        PipelineDag dag, IReadOnlyList<ModelCatalog.ModelDomain> domains)
    {
        var wanted = new List<string>();
        foreach (var node in dag.Nodes)
        {
            var n = node.Name;
            if (n.Contains("Transcribe", StringComparison.OrdinalIgnoreCase))
            { wanted.Add("whispercpp"); wanted.Add("qwen3asr"); }
            if (n.Contains("Diarization", StringComparison.OrdinalIgnoreCase))
                wanted.Add("diarization");
            if (n.Contains("Align", StringComparison.OrdinalIgnoreCase) && !n.Contains("Resolve", StringComparison.OrdinalIgnoreCase))
                wanted.Add("qwen3aligner");
            if (n.Contains("TTS", StringComparison.OrdinalIgnoreCase) || n.Contains("Profiling", StringComparison.OrdinalIgnoreCase))
                wanted.Add("qwen3tts");
            if (n.Contains("Cleaning", StringComparison.OrdinalIgnoreCase) || n.Contains("NER", StringComparison.OrdinalIgnoreCase))
                wanted.Add("bert");
        }
        return domains.Where(d => wanted.Contains(d.Name, StringComparer.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>按节点名推断相关 Provider 接口族。</summary>
    private static IReadOnlyList<string> RelevantFamilies(PipelineDag dag)
    {
        var families = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in dag.Nodes)
        {
            var n = node.Name;
            if (n.Contains("Transcribe", StringComparison.OrdinalIgnoreCase)) families.Add("Asr");
            if (n.Contains("Ocr", StringComparison.OrdinalIgnoreCase) || n.Contains("OCR", StringComparison.Ordinal)) families.Add("Ocr");
            if (n.Contains("Translation", StringComparison.OrdinalIgnoreCase)
                || n.Contains("Correct", StringComparison.OrdinalIgnoreCase)
                || n.Contains("Cleaning", StringComparison.OrdinalIgnoreCase)) families.Add("Llm");
            if (n.Contains("TTS", StringComparison.OrdinalIgnoreCase)) families.Add("Tts");
            if (n.Contains("Diarization", StringComparison.OrdinalIgnoreCase)) families.Add("Diarization");
            if (n.Contains("Vocal", StringComparison.OrdinalIgnoreCase)) families.Add("VocalSep");
        }
        return families.ToList();
    }

    private static string FriendlyFamily(IProvider p)
    {
        var iface = p.GetType().GetInterfaces()
            .FirstOrDefault(i => i.Name.StartsWith("I") && i.Name.EndsWith("Provider"))?.Name ?? "Other";
        var name = iface.StartsWith("I", StringComparison.Ordinal) ? iface[1..] : iface;
        if (name.EndsWith("Provider", StringComparison.Ordinal))
            name = name[..^"Provider".Length];
        return name switch { "VocalSeparation" => "VocalSep", _ => name };
    }
}
