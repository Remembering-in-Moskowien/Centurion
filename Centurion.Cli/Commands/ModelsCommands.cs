using Centurion.Cli.Commands.Settings;
using Centurion.Abstractions;
using Centurion.Core.Capabilities.Managers.Media;
using Centurion.Models.Console;
using Centurion.Models.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Centurion.Cli.Commands;

/// <summary>Shared model catalog helper for the models subcommands: all domains + name matching + path probing.</summary>
internal static class ModelCatalog
{
    /// <summary>A model domain (registry dictionary + local directory name).</summary>
    internal sealed record ModelDomain(string Name, string CategoryFolder, IReadOnlyDictionary<string, ModelMeta> Models);

    internal static IReadOnlyList<ModelDomain> Domains(ModelRegistry registry) =>
    [
        new("whispercpp", "whispercpp", registry.WhisperModels),
        new("fasterwhisper", "fasterwhisper", registry.FasterWhisperModels),
        new("qwen3asr", "qwen3asr", registry.Qwen3AsrModels),
        new("diarization", "diarization", registry.DiarizationModels),
        new("qwen3aligner", "qwen3aligner", registry.Qwen3ForcedAlignerModels),
        new("bert", "bert", registry.BertOnnxModels),
        new("qwen3tts", "qwen3tts", registry.Qwen3TtsModels)
    ];

    /// <summary>Matches a model name across all domains (case-insensitive); returns (domain, model name, metadata).</summary>
    internal static IReadOnlyList<(ModelDomain Domain, string ModelName, ModelMeta Meta)> Find(
        IReadOnlyList<ModelDomain> domains, string modelName)
    {
        var hits = new List<(ModelDomain, string, ModelMeta)>();
        foreach (var domain in domains)
        {
            var key = domain.Models.Keys.FirstOrDefault(k =>
                string.Equals(k, modelName, StringComparison.OrdinalIgnoreCase));
            if (key is not null)
                hits.Add((domain, key, domain.Models[key]));
        }
        return hits;
    }

    /// <summary>Creates a ModelManager (no download; only resolves local paths).</summary>
    internal static ModelManager CreateManager(
        IServiceProvider sp, ModelDomain domain, string modelName) =>
        ActivatorUtilities.CreateInstance<ModelManager>(sp, modelName, domain.Models, domain.CategoryFolder);

    /// <summary>Checks whether the local file/directory exists and is non-empty.</summary>
    internal static bool ExistsLocally(ModelManager manager)
    {
        if (!manager.ManagementEnabled || string.IsNullOrEmpty(manager.ModelFilePath))
            return false;
        if (Directory.Exists(manager.ModelFilePath))
            return Directory.EnumerateFileSystemEntries(manager.ModelFilePath).Any();
        return File.Exists(manager.ModelFilePath) && new FileInfo(manager.ModelFilePath).Length > 0;
    }
}

/// <summary>Common options for the models subcommands (includes global --json/--dry-run).</summary>
public class ModelsSettings : GlobalCommandSettings
{
}

/// <summary>models list: lists all registered models and local readiness (Spectre table + status badges).</summary>
public sealed class ModelsListCommand(
    ModelRegistry registry,
    IServiceProvider serviceProvider) : AsyncCommand<ModelsSettings>
{
    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(CommandContext context, ModelsSettings settings, CancellationToken ct)
    {
        var readyCount = 0;
        var missingCount = 0;

        var table = new Table()
            .Border(CliLayout.Border)
            .Title($"[bold]{ConsoleServices.T("Model Registry")}[/]")
            .Width(CliLayout.TableWidth())
            .AddColumn(new TableColumn(ConsoleServices.T("Domain")).LeftAligned())
            .AddColumn(new TableColumn(ConsoleServices.T("Model")).LeftAligned())
            .AddColumn(new TableColumn(ConsoleServices.T("Type")).Centered())
            .AddColumn(new TableColumn(ConsoleServices.T("State")).LeftAligned());
            // 全宽排版：标题居中与表格视觉统一

        foreach (var domain in ModelCatalog.Domains(registry))
        {
            foreach (var (name, meta) in domain.Models.OrderBy(m => m.Key, StringComparer.OrdinalIgnoreCase))
            {
                var ready = ModelCatalog.ExistsLocally(ModelCatalog.CreateManager(serviceProvider, domain, name));
                if (ready) readyCount++; else missingCount++;
                var kind = meta.DownloadType switch
                {
                    ModelDownloadType.Directory => "dir",
                    ModelDownloadType.OnnxModelDirectory => "onnx",
                    _ => "file"
                };
                var state = ready
                    ? $"[green]{CliSymbols.Dot} {ConsoleServices.T("ready")}[/]"
                    : $"[red]{CliSymbols.Ring} {ConsoleServices.T("missing")}[/]";
                table.AddRow($"[dim]{domain.Name}[/]", $"[bold]{name}[/]", kind, state);
            }
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(
            ConsoleServices.T("Total [green]{0}[/] ready / [red]{1}[/] missing", readyCount, missingCount) +
            (missingCount > 0 ? ConsoleServices.T(" — install: [cyan]Centurion models install <model>[/]") : ""));
        await Task.CompletedTask;
        return 0;
    }
}

/// <summary>Options for models install/verify/remove &lt;model&gt;.</summary>
public sealed class ModelsNameSettings : CommandSettings
{
    /// <summary>Model name (whisper domain: tiny/base/...; qwen3asr: qwen3-asr-0.6b, etc.).</summary>
    [CommandArgument(0, "<model>")]
    public string Model { get; set; } = string.Empty;
}

/// <summary>models install: downloads the given model (fetched per registry metadata when missing).</summary>
public sealed class ModelsInstallCommand(
    ModelRegistry registry,
    IServiceProvider serviceProvider) : AsyncCommand<ModelsNameSettings>
{
    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(CommandContext context, ModelsNameSettings settings, CancellationToken ct)
    {
        var hits = ModelCatalog.Find(ModelCatalog.Domains(registry), settings.Model);
        if (hits.Count == 0)
        {
            ConsoleServices.Output.WriteError($"Unknown model '{settings.Model}'. Run 'Centurion models list' to see registered models.");
            return ExitCodes.Failure;
        }
        if (hits.Count > 1)
        {
            ConsoleServices.Output.WriteError(
                $"Model '{settings.Model}' is ambiguous: " +
                string.Join(", ", hits.Select(h => $"{h.Domain.Name}/{h.ModelName}")) + ".");
            return ExitCodes.Failure;
        }

        var (domain, modelName, _) = hits[0];
        var manager = ModelCatalog.CreateManager(serviceProvider, domain, modelName);
        ConsoleServices.Output.WriteInfo($"Installing model '{domain.Name}/{modelName}' ...");
        await manager.EnsureInstalledAsync(ct);
        ConsoleServices.Output.WriteSuccess($"Installed: {manager.ModelFilePath}");
        return 0;
    }
}

/// <summary>models verify: checks local model files are ready.</summary>
public sealed class ModelsVerifyCommand(
    ModelRegistry registry,
    IServiceProvider serviceProvider) : AsyncCommand<ModelsNameSettings>
{
    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(CommandContext context, ModelsNameSettings settings, CancellationToken ct)
    {
        var hits = ModelCatalog.Find(ModelCatalog.Domains(registry), settings.Model);
        if (hits.Count == 0)
        {
            ConsoleServices.Output.WriteError($"Unknown model '{settings.Model}'. Run 'Centurion models list' to see registered models.");
            return ExitCodes.Failure;
        }
        if (hits.Count > 1)
        {
            ConsoleServices.Output.WriteError(
                $"Model '{settings.Model}' is ambiguous: " +
                string.Join(", ", hits.Select(h => $"{h.Domain.Name}/{h.ModelName}")) + ".");
            return ExitCodes.Failure;
        }

        var (domain, modelName, _) = hits[0];
        var manager = ModelCatalog.CreateManager(serviceProvider, domain, modelName);
        var ready = ModelCatalog.ExistsLocally(manager);

        var table = new Table()
            .Border(CliLayout.Border)
            .AddColumn(new TableColumn(ConsoleServices.T("Property")).Width(12))
            .AddColumn(new TableColumn(ConsoleServices.T("Value")));
        table.AddRow(ConsoleServices.T("Model"), $"[bold]{domain.Name}/{modelName}[/]");
        table.AddRow(ConsoleServices.T("Path"), $"[dim]{manager.ModelFilePath}[/]");
        table.AddRow(ConsoleServices.T("State"), ready ? "[green]● ready[/]" : "[red]○ missing[/]");
        if (!ready)
            table.AddRow(ConsoleServices.T("Install"), "[cyan]Centurion models install " + modelName + "[/]");
        AnsiConsole.Write(table);
        await Task.CompletedTask;
        return ready ? 0 : 1;
    }
}

/// <summary>models remove: deletes local model files/directories.</summary>
public sealed class ModelsRemoveCommand(
    ModelRegistry registry,
    IServiceProvider serviceProvider) : AsyncCommand<ModelsNameSettings>
{
    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(CommandContext context, ModelsNameSettings settings, CancellationToken ct)
    {
        var hits = ModelCatalog.Find(ModelCatalog.Domains(registry), settings.Model);
        if (hits.Count == 0)
        {
            ConsoleServices.Output.WriteError($"Unknown model '{settings.Model}'. Run 'Centurion models list' to see registered models.");
            return ExitCodes.Failure;
        }
        if (hits.Count > 1)
        {
            ConsoleServices.Output.WriteError(
                $"Model '{settings.Model}' is ambiguous: " +
                string.Join(", ", hits.Select(h => $"{h.Domain.Name}/{h.ModelName}")) + ".");
            return ExitCodes.Failure;
        }

        var (domain, modelName, _) = hits[0];
        var manager = ModelCatalog.CreateManager(serviceProvider, domain, modelName);
        if (!ModelCatalog.ExistsLocally(manager))
        {
            ConsoleServices.Output.WriteLine($"  {domain.Name}/{modelName}: not present locally ({manager.ModelFilePath}); nothing to remove.");
            return 0;
        }

        if (!await ConsoleServices.Confirm.ConfirmAsync($"Remove model files under '{manager.ModelFilePath}'? [y/N]"))
            return 0;

        if (Directory.Exists(manager.ModelFilePath))
            Directory.Delete(manager.ModelFilePath, recursive: true);
        else if (File.Exists(manager.ModelFilePath))
            File.Delete(manager.ModelFilePath);
        ConsoleServices.Output.WriteSuccess($"Removed: {manager.ModelFilePath}");
        return 0;
    }
}
