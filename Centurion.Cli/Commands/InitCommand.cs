using Centurion.Cli.Commands.Settings;
using Centurion.Abstractions;
using Centurion.Core.Providers;
using Centurion.Models.Console;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;
using System.Linq;
using System.Text.Json;

namespace Centurion.Cli.Commands;

/// <summary>
/// <c>init</c> 命令：交互式向导，为新手生成 <c>centurion.config.json</c> 与推荐命令链，
/// 3 条命令即可完成「转写 → 翻译 → 出字幕」的完整链路。
/// </summary>
public sealed class InitCommand(
    ILogger<InitCommand> logger) : AsyncCommand<InitSettings>
{
    private static readonly string[] Workflows = ["asr", "ocr", "from-script", "translate", "dub", "correct"];
    private static readonly string[] Formats = ["ass", "srt", "txt"];
    private static readonly string[] Profiles = ["offline", "fast", "quality", "cheap"];

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(CommandContext context, InitSettings settings, CancellationToken ct)
    {
        try
        {
            var interactive = !settings.Yes && !System.Console.IsInputRedirected;

            // 1) 收集选项（缺省时交互提问，非交互用默认值）
            var media = settings.Media?.FullName ?? AskText(interactive, ConsoleServices.T("Media file path (video/audio)"), "samples/test.mp4");
            var workflow = (settings.Workflow ?? AskChoice(interactive, ConsoleServices.T("Select workflow"), Workflows, "asr")).ToLowerInvariant();
            var format = (settings.Format ?? AskChoice(interactive, ConsoleServices.T("Output subtitle format"), Formats, "ass")).ToLowerInvariant();
            var profile = (settings.Profile ?? AskChoice(interactive, ConsoleServices.T("Provider profile"), Profiles, "offline")).ToLowerInvariant();
            var target = settings.Target ?? (workflow is "translate" or "dub"
                ? AskText(interactive, ConsoleServices.T("Translation target language (e.g. zh / en)"), "zh")
                : null);

            if (!Workflows.Contains(workflow))
                throw new ArgumentException(ConsoleServices.T("Unknown workflow '{0}'. Supported: {1}", workflow, string.Join(", ", Workflows)));
            if (!Formats.Contains(format))
                throw new ArgumentException(ConsoleServices.T("Unknown format '{0}'. Supported: {1}", format, string.Join(", ", Formats)));
            if (!Profiles.Contains(profile))
                throw new ArgumentException(ConsoleServices.T("Unknown profile '{0}'. Supported: {1}", profile, string.Join(", ", Profiles)));
            if (!File.Exists(media))
            {
                ConsoleServices.Output.WriteWarning(ConsoleServices.T("Media file does not exist (config written anyway; path can be replaced later): {0}", media));
            }

            // 2) 写入 centurion.config.json
            var outputDir = settings.Output?.FullName ?? Directory.GetCurrentDirectory();
            Directory.CreateDirectory(outputDir);
            var configPath = Path.Combine(outputDir, "centurion.config.json");
            var recipe = BuildRecipe(media, workflow, format, target);
            var config = new CenturionConfig
            {
                Profile = profile,
                Language = null,
                OutputFormat = format,
                Recipe = recipe
            };
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
            await File.WriteAllTextAsync(configPath, json, ct);

            // 3) 立即应用 profile
            ProviderProfileResolver.Current = ProviderProfileResolver.FromString(profile);

            // 4) 输出引导
            AnsiConsole.Write(new Rule($"[bold green]{ConsoleServices.T("Configuration complete")}[/]").RuleStyle("green"));
            ConsoleServices.Output.WriteMarkupLine(ConsoleServices.T("Generated [bold]{0}[/] (profile={1}, format={2})", configPath, profile, format));

            var lines = recipe.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var table = new Table()
                .Border(CliLayout.Border)
                .Title($"[bold]{ConsoleServices.T("Recommended command chain")} · {workflow}[/]")
                .Width(CliLayout.TableWidth())
                .AddColumn(new TableColumn("#").Centered())
                .AddColumn(new TableColumn(ConsoleServices.T("Command")).LeftAligned());
            for (var i = 0; i < lines.Length; i++)
                table.AddRow($"[dim]{i + 1}[/]", $"[cyan]{lines[i]}[/]");
            AnsiConsole.Write(table);
            ConsoleServices.Output.WriteMarkupLine(
                $"[dim]{ConsoleServices.T("Run them in order; add --help to any step for options. More examples in the samples/ directory.")}[/]");
            return ExitCodes.Success;
        }
        catch (Exception ex)
        {
            CliErrorPrinter.Print(logger, ex, "init wizard failed.");
            return ExitCodes.Failure;
        }
    }

    /// <summary>交互提问文本（非交互返回默认值）。</summary>
    private static string AskText(bool interactive, string prompt, string defaultValue)
        => interactive
            ? AnsiConsole.Prompt(new TextPrompt<string>($"[bold]{prompt}[/]").DefaultValue(defaultValue))
            : defaultValue;

    /// <summary>交互选择（非交互返回默认值）。</summary>
    private static string AskChoice(bool interactive, string prompt, string[] choices, string defaultValue)
        => interactive
            ? AnsiConsole.Prompt(new SelectionPrompt<string>()
                .Title($"[bold]{prompt}[/]")
                .AddChoices(choices))
            : defaultValue;

    /// <summary>按工作流生成推荐命令链（3 条以内）。</summary>
    internal static string BuildRecipe(string media, string workflow, string format, string? target)
    {
        var baseName = Path.GetFileNameWithoutExtension(media);
        return workflow switch
        {
            "asr" => string.Join('\n', new[]
            {
                $"Centurion asr {media}",
                $"Centurion translate {baseName}.centurion.json --target {target ?? "zh"}",
                $"Centurion build {baseName}.centurion.json --format {format}"
            }),
            "ocr" => string.Join('\n', new[]
            {
                $"Centurion ocr {media}",
                $"Centurion translate {baseName}.centurion.json --target {target ?? "zh"}",
                $"Centurion build {baseName}.centurion.json --format {format}"
            }),
            "from-script" => string.Join('\n', new[]
            {
                $"Centurion from-script {media} <script>",
                $"Centurion translate {baseName}.centurion.json --target {target ?? "zh"}",
                $"Centurion build {baseName}.centurion.json --format {format}"
            }),
            "translate" => string.Join('\n', new[]
            {
                $"Centurion translate {baseName}.centurion.json --target {target ?? "zh"}",
                $"Centurion build {baseName}.centurion.json --format {format}"
            }),
            "dub" => string.Join('\n', new[]
            {
                $"Centurion translate {baseName}.centurion.json --target {target ?? "zh"}",
                $"Centurion dub {baseName}.centurion.json"
            }),
            "correct" => string.Join('\n', new[]
            {
                $"Centurion correct {baseName}.centurion.json",
                $"Centurion build {baseName}.centurion.json --format {format}"
            }),
            _ => string.Join('\n', new[] { $"Centurion asr {media}", $"Centurion build {baseName}.centurion.json --format {format}" })
        };
    }
}
