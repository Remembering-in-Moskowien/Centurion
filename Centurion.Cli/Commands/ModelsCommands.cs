using Centurion.Core.Capabilities.Managers.Media;
using Centurion.Models.Console;
using Centurion.Models.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Centurion.Cli.Commands;

/// <summary>models 子命令的共享模型目录工具：全部模型域 + 按名匹配 + 路径探测。</summary>
internal static class ModelCatalog
{
    /// <summary>一个模型域（注册表字典 + 本地目录名）。</summary>
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

    /// <summary>按模型名（大小写不敏感）在全部域中匹配；返回 (域, 模型名, 元数据)。</summary>
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

    /// <summary>创建 ModelManager（不触发下载，仅解析本地路径）。</summary>
    internal static ModelManager CreateManager(
        IServiceProvider sp, ModelDomain domain, string modelName) =>
        ActivatorUtilities.CreateInstance<ModelManager>(sp, modelName, domain.Models, domain.CategoryFolder);

    /// <summary>检查本地文件/目录是否存在且非空。</summary>
    internal static bool ExistsLocally(ModelManager manager)
    {
        if (!manager.ManagementEnabled || string.IsNullOrEmpty(manager.ModelFilePath))
            return false;
        if (Directory.Exists(manager.ModelFilePath))
            return Directory.EnumerateFileSystemEntries(manager.ModelFilePath).Any();
        return File.Exists(manager.ModelFilePath) && new FileInfo(manager.ModelFilePath).Length > 0;
    }
}

/// <summary>models 子命令的公共选项（无全局参数）。</summary>
public class ModelsSettings : CommandSettings
{
}

/// <summary>models list：列出全部注册模型与本地状态（Spectre 表格 + 状态徽章）。</summary>
public sealed class ModelsListCommand(
    ModelRegistry registry,
    IServiceProvider serviceProvider) : AsyncCommand<ModelsSettings>
{
    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(CommandContext context, ModelsSettings settings, CancellationToken ct)
    {
        var readyCount = 0;
        var missingCount = 0;

        foreach (var domain in ModelCatalog.Domains(registry))
        {
            var table = new Table()
                .Border(TableBorder.Rounded)
                .Title($"[bold]{domain.Name}[/] ({domain.Models.Count} models)")
                .AddColumn(new TableColumn("模型").LeftAligned())
                .AddColumn(new TableColumn("类型").Width(6))
                .AddColumn(new TableColumn("状态").Width(10));

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
                    ? "[green]● ready[/]"
                    : "[red]○ missing[/]";
                table.AddRow($"[bold]{name}[/]", kind, state);
            }

            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();
        }

        AnsiConsole.MarkupLine(
            $"总计 [green]{readyCount}[/] 就绪 / [red]{missingCount}[/] 缺失" +
            (missingCount > 0 ? "  — 安装: [cyan]Centurion models install <model>[/]" : ""));
        await Task.CompletedTask;
        return 0;
    }
}

/// <summary>models install/verify/remove &lt;model&gt; 的选项。</summary>
public sealed class ModelsNameSettings : CommandSettings
{
    /// <summary>模型名（whisper 域：tiny/base/...；qwen3asr：qwen3-asr-0.6b 等）。</summary>
    [CommandArgument(0, "<model>")]
    public string Model { get; set; } = string.Empty;
}

/// <summary>models install：下载指定模型（缺失时按注册表元数据拉取）。</summary>
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
            return 1;
        }
        if (hits.Count > 1)
        {
            ConsoleServices.Output.WriteError(
                $"Model '{settings.Model}' is ambiguous: " +
                string.Join(", ", hits.Select(h => $"{h.Domain.Name}/{h.ModelName}")) + ".");
            return 1;
        }

        var (domain, modelName, _) = hits[0];
        var manager = ModelCatalog.CreateManager(serviceProvider, domain, modelName);
        ConsoleServices.Output.WriteInfo($"Installing model '{domain.Name}/{modelName}' ...");
        await manager.EnsureInstalledAsync(ct);
        ConsoleServices.Output.WriteSuccess($"Installed: {manager.ModelFilePath}");
        return 0;
    }
}

/// <summary>models verify：校验模型本地文件是否就绪。</summary>
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
            return 1;
        }
        if (hits.Count > 1)
        {
            ConsoleServices.Output.WriteError(
                $"Model '{settings.Model}' is ambiguous: " +
                string.Join(", ", hits.Select(h => $"{h.Domain.Name}/{h.ModelName}")) + ".");
            return 1;
        }

        var (domain, modelName, _) = hits[0];
        var manager = ModelCatalog.CreateManager(serviceProvider, domain, modelName);
        var ready = ModelCatalog.ExistsLocally(manager);

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn(new TableColumn("属性").Width(12))
            .AddColumn(new TableColumn("值"));
        table.AddRow("模型", $"[bold]{domain.Name}/{modelName}[/]");
        table.AddRow("路径", $"[dim]{manager.ModelFilePath}[/]");
        table.AddRow("状态", ready ? "[green]● ready[/]" : "[red]○ missing[/]");
        if (!ready)
            table.AddRow("安装", "[cyan]Centurion models install " + modelName + "[/]");
        AnsiConsole.Write(table);
        await Task.CompletedTask;
        return ready ? 0 : 1;
    }
}

/// <summary>models remove：删除模型本地文件/目录。</summary>
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
            return 1;
        }
        if (hits.Count > 1)
        {
            ConsoleServices.Output.WriteError(
                $"Model '{settings.Model}' is ambiguous: " +
                string.Join(", ", hits.Select(h => $"{h.Domain.Name}/{h.ModelName}")) + ".");
            return 1;
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
