using Centurion.Abstractions.Providers;
using Centurion.Models.Console;
using Centurion.Models.Providers;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Centurion.Cli.Commands;

/// <summary>providers 子命令的公共选项。</summary>
public class ProvidersSettings : CommandSettings
{
}

/// <summary>providers list：列出全部已注册 Provider 及其能力与可用性（Spectre 表格 + 成本图表）。</summary>
public sealed class ProvidersListCommand(
    IProviderRegistry registry) : AsyncCommand<ProvidersSettings>
{
    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(CommandContext context, ProvidersSettings settings, CancellationToken ct)
    {
        var groups = registry.All
            .GroupBy(p => p.GetType().GetInterfaces().FirstOrDefault(i => i.Name.StartsWith("I") && i.Name.EndsWith("Provider"))?.Name ?? "Other")
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

        var all = new List<(IProvider Provider, bool Available)>();
        foreach (var group in groups)
        {
            var table = new Table()
                .Border(TableBorder.Rounded)
                .Title($"[bold]{group.Key}[/] ({group.Count()} providers)")
                .AddColumn(new TableColumn("Provider").LeftAligned())
                .AddColumn(new TableColumn("Kind").Width(8))
                .AddColumn(new TableColumn("成本 $/1M tok").RightAligned())
                .AddColumn(new TableColumn("$/音频分钟").RightAligned())
                .AddColumn(new TableColumn("延迟").Width(9))
                .AddColumn(new TableColumn("质量").Width(9))
                .AddColumn(new TableColumn("状态").Width(12));

            foreach (var provider in group.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                var c = provider.Capabilities;
                var available = await provider.IsAvailableAsync(ct);
                all.Add((provider, available));
                var status = available ? "[green]● 可用[/]" : "[red]○ 不可用[/]";
                table.AddRow(
                    $"[bold]{provider.Name}[/]",
                    $"{c.Kind}",
                    $"{c.CostPer1MTokensUsd,10:F2}",
                    $"{c.CostPerAudioMinuteUsd,10:F4}",
                    $"{c.Latency}",
                    $"{c.Quality}",
                    status);
            }

            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();
        }

        // 成本对比图：每 1M token 成本（本地为 0，直观展示云/本地成本差）
        var chart = new BarChart()
            .Width(64)
            .Label("成本对比 — $/1M tokens")
            .CenterLabel();
        foreach (var (provider, _) in all.OrderByDescending(p => p.Provider.Capabilities.CostPer1MTokensUsd))
        {
            chart.AddItem(provider.DisplayName, provider.Capabilities.CostPer1MTokensUsd);
        }
        AnsiConsole.Write(chart);

        var availableCount = all.Count(p => p.Available);
        AnsiConsole.MarkupLine(
            $"可用 [green]{availableCount}[/]/{all.Count} — 探测: [cyan]Centurion providers test <name>[/]");
        return 0;
    }
}

/// <summary>providers test &lt;name&gt; 的选项。</summary>
public sealed class ProvidersTestSettings : CommandSettings
{
    /// <summary>Provider 名称（见 providers list；如 whispercpp / openai / zhipu / ollama / llama-tts）。</summary>
    [CommandArgument(0, "<name>")]
    public string Name { get; set; } = string.Empty;
}

/// <summary>providers test：探测指定 Provider 的可用性并显示能力声明。</summary>
public sealed class ProvidersTestCommand(
    IProviderRegistry registry) : AsyncCommand<ProvidersTestSettings>
{
    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(CommandContext context, ProvidersTestSettings settings, CancellationToken ct)
    {
        var provider = registry.Find(settings.Name);
        if (provider is null)
        {
            ConsoleServices.Output.WriteError(
                $"Unknown provider '{settings.Name}'. Run 'Centurion providers list' to see registered providers.");
            return 1;
        }

        var c = provider.Capabilities;
        var available = await provider.IsAvailableAsync(ct);

        var table = new Table()
            .Border(TableBorder.Rounded)
            .Title($"[bold]{provider.Name}[/] — {provider.DisplayName}")
            .AddColumn(new TableColumn("属性").Width(12))
            .AddColumn(new TableColumn("值"));
        table.AddRow("类型", $"{c.Kind}");
        table.AddRow("语言", string.Join(", ", c.SupportedLanguages));
        table.AddRow("GPU", c.RequiresGpu ? "required" : "optional");
        table.AddRow("延迟", $"{c.Latency}");
        table.AddRow("质量", $"{c.Quality}");
        table.AddRow("成本", $"{c.CostPerAudioMinuteUsd:F4} $/音频分钟 · {c.CostPer1MTokensUsd:F2} $/1M tokens");
        table.AddRow("说明", c.Description);
        table.AddRow("状态", available
            ? "[green]● 可用[/]"
            : "[red]○ 不可用 (missing API key or local service not running)[/]");
        AnsiConsole.Write(table);
        return available ? 0 : 1;
    }
}
