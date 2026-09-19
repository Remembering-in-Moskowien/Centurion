using Centurion.Models.Console;
using System.Diagnostics;
using Centurion.Cli.Commands.Settings;
using Centurion.Core.Infrastructure;
using Centurion.Core.Update;
using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;

namespace Centurion.Cli.Commands;

/// <summary>
/// 自更新命令：从 GitHub Releases 检查 / 下载 / 应用新版本。
/// </summary>
public sealed class UpdateCommand(
    IUpdateService updateService,
    ILogger<UpdateCommand> logger) : AsyncCommand<UpdateSettings>
{
    /// <summary>
    /// 执行自更新：检查新版本、按选项下载并可选择立即应用更新。
    /// </summary>
    /// <param name="context">Spectre 命令上下文。</param>
    /// <param name="settings">更新命令选项。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    protected override async Task<int> ExecuteAsync(CommandContext context, UpdateSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            ConsoleServices.Output.WriteMarkupLine($"[grey]Current version: {updateService.LocalVersion}[/]");
            var check = await updateService.CheckAsync(cancellationToken);

            if (!check.HasUpdate)
            {
                if (check.Reason is not null)
                    ConsoleServices.Output.WriteWarning(check.Reason);
                else
                    ConsoleServices.Output.WriteSuccess("You're on the latest version — all good. 🎉");
                return 0;
            }

            var release = check.Latest!;
            ConsoleServices.Output.WriteMarkupLine($"[bold cyan]New version available: {release.TagName}[/] [grey]({release.PublishedAt:yyyy-MM-dd})[/]");
            if (!string.IsNullOrWhiteSpace(release.Body))
            {
                var excerpt = release.Body.Split('\n')
                    .Select(line => line.Trim())
                    .Where(line => line.Length > 0 && !line.StartsWith("#", StringComparison.Ordinal))
                    .Take(3);
                foreach (var line in excerpt)
                    ConsoleServices.Output.WriteMarkupLine($"[grey]  {EscapeMarkup(line)}[/]");
            }

            if (settings.CheckOnly)
            {
                ConsoleServices.Output.WriteInfo("Check only — nothing downloaded. Run again without --check to update.");
                return 0;
            }

            var rid = GitHubUpdateService.GetRuntimeIdentifier();
            var asset = release.Assets.FirstOrDefault(a =>
                string.Equals(a.Name, settings.AssetName ?? $"Centurion-{rid}.zip", StringComparison.OrdinalIgnoreCase))
                ?? release.Assets.FirstOrDefault(a =>
                    a.Name.Contains(rid, StringComparison.OrdinalIgnoreCase) && a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

            if (asset is null)
            {
                ConsoleServices.Output.WriteError($"No matching release asset for '{rid}'. Available: {string.Join(", ", release.Assets.Select(a => a.Name))}");
                return 1;
            }

            ConsoleServices.Output.WriteInfo($"Downloading {asset.Name} ({FormatSize(asset.SizeBytes)})...");
            var stage = await updateService.StageAsync(check, settings.AssetName, cancellationToken);
            ConsoleServices.Output.WriteSuccess($"Update staged: {stage.PayloadDirectory}");

            if (settings.Apply)
            {
                ConsoleServices.Output.WriteInfo("Launching the apply script — the program will close and restart automatically. 🔄");
                Process.Start(new ProcessStartInfo(stage.ScriptPath) { UseShellExecute = true, WorkingDirectory = AppContext.BaseDirectory });
                return 0;
            }

            ConsoleServices.Output.WriteMarkupLine($"Run [green]{stage.ScriptPath}[/] to finish the update (it will close & restart this program).");
            return 0;
        }
        catch (OperationCanceledException)
        {
            ConsoleServices.Output.WriteWarning("Update cancelled.");
            return 1;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Update failed.");
            ConsoleServices.Output.WriteError($"Update failed: {ex.Message}");
            return 1;
        }
    }

    private static string EscapeMarkup(string text)
        => text.Replace("[", "[[").Replace("]", "]]");

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1 << 30 => $"{bytes / (double)(1 << 30):F2} GB",
        >= 1 << 20 => $"{bytes / (double)(1 << 20):F2} MB",
        >= 1 << 10 => $"{bytes / (double)(1 << 10):F2} KB",
        _ => $"{bytes} B"
    };
}
