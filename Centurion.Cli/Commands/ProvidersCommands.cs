using Centurion.Abstractions.Providers;
using Centurion.Models.Console;
using Centurion.Models.Providers;
using Spectre.Console.Cli;

namespace Centurion.Cli.Commands;

/// <summary>providers 子命令的公共选项。</summary>
public class ProvidersSettings : CommandSettings
{
}

/// <summary>providers list：列出全部已注册 Provider 及其能力与可用性。</summary>
public sealed class ProvidersListCommand(
    IProviderRegistry registry) : AsyncCommand<ProvidersSettings>
{
    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(CommandContext context, ProvidersSettings settings, CancellationToken ct)
    {
        var groups = registry.All
            .GroupBy(p => p.GetType().GetInterfaces().FirstOrDefault(i => i.Name.StartsWith("I") && i.Name.EndsWith("Provider"))?.Name ?? "Other")
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            ConsoleServices.Output.WriteLine($"[{group.Key}] {group.Count()} providers");
            foreach (var provider in group.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                var c = provider.Capabilities;
                var available = await provider.IsAvailableAsync(ct);
                ConsoleServices.Output.WriteLine(
                    $"  {provider.Name,-16} {c.Kind,-6} cost ${c.CostPer1MTokensUsd:F2}/1M tok" +
                    $" / ${c.CostPerAudioMinuteUsd:F4}/min  latency {c.Latency,-7} quality {c.Quality,-7} " +
                    (available ? "available" : "UNAVAILABLE"));
                if (c.Description.Length > 0)
                    ConsoleServices.Output.WriteLine($"    {c.Description}");
            }
        }
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
        ConsoleServices.Output.WriteLine($"provider : {provider.Name} ({provider.DisplayName})");
        ConsoleServices.Output.WriteLine($"kind     : {c.Kind}");
        ConsoleServices.Output.WriteLine($"languages: {string.Join(", ", c.SupportedLanguages)}");
        ConsoleServices.Output.WriteLine($"gpu      : {(c.RequiresGpu ? "required" : "optional")}");
        ConsoleServices.Output.WriteLine($"latency  : {c.Latency}");
        ConsoleServices.Output.WriteLine($"quality  : {c.Quality}");
        ConsoleServices.Output.WriteLine($"cost     : ${c.CostPerAudioMinuteUsd:F4}/audio-min, ${c.CostPer1MTokensUsd:F2}/1M tokens");
        ConsoleServices.Output.WriteLine($"desc     : {c.Description}");

        var available = await provider.IsAvailableAsync(ct);
        ConsoleServices.Output.WriteLine(available
            ? "status   : available"
            : "status   : UNAVAILABLE (missing API key or local service not running)");
        return available ? 0 : 1;
    }
}
