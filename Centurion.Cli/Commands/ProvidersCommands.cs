using Centurion.Cli.Commands.Settings;
using Centurion.Abstractions;
using Centurion.Abstractions.Providers;
using Centurion.Models.Console;
using Centurion.Models.Providers;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Centurion.Cli.Commands;

/// <summary>Common options for the providers subcommands.</summary>
public class ProvidersSettings : GlobalCommandSettings
{
}

/// <summary>providers list: lists all registered providers with capabilities and availability (Spectre table + cost chart).</summary>
public sealed class ProvidersListCommand(
    IProviderRegistry registry) : AsyncCommand<ProvidersSettings>
{
    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(CommandContext context, ProvidersSettings settings, CancellationToken ct)
    {
        var groups = registry.All
            .GroupBy(p => FriendlyFamily(
                p.GetType().GetInterfaces()
                    .FirstOrDefault(i => i.Name.StartsWith("I") && i.Name.EndsWith("Provider"))?.Name))
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

        var all = new List<(IProvider Provider, bool Available)>();
        var table = new Table()
            .Border(CliLayout.Border)
            .Title($"[bold]{ConsoleServices.T("Provider Registry")}[/]")
            .Width(CliLayout.TableWidth())
            .AddColumn(new TableColumn(ConsoleServices.T("Family")).LeftAligned())
            .AddColumn(new TableColumn(ConsoleServices.T("Provider")).LeftAligned().Width(18))
            .AddColumn(new TableColumn("$/1M tok").RightAligned())
            .AddColumn(new TableColumn(ConsoleServices.T("Quality")).Centered())
            .AddColumn(new TableColumn(ConsoleServices.T("State")).LeftAligned());

        foreach (var group in groups)
        {
            foreach (var provider in group.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                var c = provider.Capabilities;
                var available = await provider.IsAvailableAsync(ct);
                all.Add((provider, available));
                var status = available
                    ? $"[green]{CliSymbols.Dot} {ConsoleServices.T("available")}[/]"
                    : $"[red]{CliSymbols.Ring} {ConsoleServices.T("unavailable")}[/]";
                table.AddRow(
                    $"[dim]{group.Key}[/]",
                    $"[bold]{provider.Name}[/]",
                    $"{c.CostPer1MTokensUsd:F2}",
                    $"{c.Quality}",
                    status);
            }
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"[dim]{ConsoleServices.T("Legend: L=local (0 cost) · C=cloud (API), details: providers test <name>")}[/]");
        AnsiConsole.WriteLine();

        // 成本对比图：每 1M token 成本（本地为 0，直观展示云/本地成本差）
        var chart = new BarChart()
            .Width(64)
            .Label(ConsoleServices.T("Cost comparison — $/1M tokens"))
            .CenterLabel();
        foreach (var (provider, _) in all.OrderByDescending(p => p.Provider.Capabilities.CostPer1MTokensUsd))
        {
            chart.AddItem(provider.DisplayName, provider.Capabilities.CostPer1MTokensUsd);
        }
        AnsiConsole.Write(chart);

        var availableCount = all.Count(p => p.Available);
        AnsiConsole.MarkupLine(
            ConsoleServices.T("Available [green]{0}[/]/{1} — probe: [cyan]Centurion providers test <name>[/]", availableCount, all.Count));
        return 0;
    }

    /// <summary>Maps a provider interface name to a friendly family name (IAsrProvider → ASR).</summary>
    private static string FriendlyFamily(string? interfaceName)
    {
        if (string.IsNullOrWhiteSpace(interfaceName))
            return "Other";
        var name = interfaceName.StartsWith("I", StringComparison.Ordinal)
            ? interfaceName[1..]
            : interfaceName;
        if (name.EndsWith("Provider", StringComparison.Ordinal))
            name = name[..^"Provider".Length];
        return name switch
        {
            "VocalSeparation" => "VocalSep",
            _ => name
        };
    }
}

/// <summary>Options for providers test &lt;name&gt;.</summary>
public sealed class ProvidersTestSettings : GlobalCommandSettings
{
    /// <summary>Provider name (see providers list; e.g. whispercpp / openai / zhipu / ollama / llama-tts).</summary>
    [CommandArgument(0, "<name>")]
    public string Name { get; set; } = string.Empty;
}

/// <summary>providers test: probes a provider's availability and shows its capability declaration.</summary>
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
            return ExitCodes.Failure;
        }

        var c = provider.Capabilities;
        var available = await provider.IsAvailableAsync(ct);

        var table = new Table()
            .Border(CliLayout.Border)
            .Title($"[bold]{provider.Name}[/] — {provider.DisplayName}")
            .AddColumn(new TableColumn(ConsoleServices.T("Property")).Width(12))
            .AddColumn(new TableColumn(ConsoleServices.T("Value")));
        table.AddRow(ConsoleServices.T("Type"), $"{c.Kind}");
        table.AddRow(ConsoleServices.T("Language"), string.Join(", ", c.SupportedLanguages));
        table.AddRow("GPU", c.RequiresGpu ? "required" : "optional");
        table.AddRow(ConsoleServices.T("Latency"), $"{c.Latency}");
        table.AddRow(ConsoleServices.T("Quality"), $"{c.Quality}");
        table.AddRow(ConsoleServices.T("Cost"), $"{c.CostPerAudioMinuteUsd:F4} $/min · {c.CostPer1MTokensUsd:F2} $/1M tokens");
        table.AddRow(ConsoleServices.T("Description"), c.Description);
        table.AddRow(ConsoleServices.T("State"), available
            ? $"[green]{CliSymbols.Dot} {ConsoleServices.T("available")}[/]"
            : $"[red]{CliSymbols.Ring} {ConsoleServices.T("unavailable")} ({ConsoleServices.T("missing API key or local service not running")})[/]");
        AnsiConsole.Write(table);
        return available ? 0 : 1;
    }
}
