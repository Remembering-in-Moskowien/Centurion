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
/// <c>--dry-run</c> preview: after the DAG is built, prints the topology to be
/// executed, involved models (local readiness), related provider unit prices and the
/// profile orientation, without running any operator. JSON mode emits an isomorphic
/// structure for scripts.
/// </summary>
public static class DryRunHelper
{
    /// <summary>Prints the dry-run preview (console tree + tables, or a single JSON line with --json).</summary>
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

        ConsoleServices.Output.WriteMarkupLine($"[bold cyan]{CliSymbols.Play} {ConsoleServices.T("Dry-run")}[/] {ConsoleServices.T("command")} [bold]{config.CommandName}[/] {CliSymbols.MidDot} profile=[gold]{profileName}[/]");
        AnsiConsole.Write(PipelineGraphRenderer.RenderTree(dag));

        if (models.Count > 0)
        {
            var table = new Table()
                .Border(CliLayout.Border)
                .Title($"[bold]{ConsoleServices.T("Models involved")}[/]")
                .Width(CliLayout.TableWidth())
                .AddColumn(new TableColumn(ConsoleServices.T("Domain")).LeftAligned())
                .AddColumn(new TableColumn(ConsoleServices.T("Model")).LeftAligned())
                .AddColumn(new TableColumn(ConsoleServices.T("State")).LeftAligned());
            foreach (var m in models.Cast<dynamic>())
                table.AddRow($"[dim]{m.domain}[/]", $"[bold]{m.model}[/]",
                    m.ready ? $"[green]{CliSymbols.Dot} ready[/]" : $"[red]{CliSymbols.Ring} missing[/]");
            AnsiConsole.Write(table);
        }

        if (providers.Count > 0)
        {
            var ptable = new Table()
                .Border(CliLayout.Border)
                .Title($"[bold]{ConsoleServices.T("Related providers (unit price; total billed by actual duration/tokens)")}[/]")
                .Width(CliLayout.TableWidth())
                .AddColumn(new TableColumn(ConsoleServices.T("Family")).LeftAligned())
                .AddColumn(new TableColumn("Provider").LeftAligned().Width(18))
                .AddColumn(new TableColumn(ConsoleServices.T("Type")).Centered())
                .AddColumn(new TableColumn("$/1M tok").RightAligned())
                .AddColumn(new TableColumn("$/min").RightAligned())
                .AddColumn(new TableColumn(ConsoleServices.T("Quality")).Centered());
            foreach (var p in providers.Cast<dynamic>())
                ptable.AddRow($"[dim]{p.family}[/]", $"[bold]{p.name}[/]",
                    p.kind == "Cloud" ? "[cyan]C[/]" : "[dim]L[/]",
                    $"{p.costPer1MTokensUsd:F2}", $"{p.costPerAudioMinuteUsd:F4}", $"{p.quality}");
            AnsiConsole.Write(ptable);
        }

        ConsoleServices.Output.WriteInfo(ConsoleServices.T("Dry-run: no operators executed. Run again without --dry-run to execute."));
        await Task.CompletedTask;
        return ExitCodes.Success;
    }

    /// <summary>Infers the relevant model domains from node names (case-insensitive substring match).</summary>
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

    /// <summary>Infers the relevant provider interface family from a node name.</summary>
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
