using System.Diagnostics;
using Centurion.Abstractions;
using Centurion.Cli.Commands.Settings;
using Centurion.Core.Capabilities.Infrastructure;using Centurion.Core.Capabilities.Update;using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;
using Centurion.Abstractions.Utils;
using Centurion.Models.Console;
namespace Centurion.Cli.Commands;

/// <summary>
/// Self-update command: check / download / apply new versions from GitHub Releases.
/// </summary>
public sealed class UpdateCommand(
    IUpdateService updateService,
    ILogger<UpdateCommand> logger) : AsyncCommand<UpdateSettings>
{
    /// <summary>
    /// Runs self-update: checks for new versions, downloads per options and optionally applies immediately.
    /// </summary>
    /// <param name="context">The Spectre command context.</param>
    /// <param name="settings">The update command settings.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<int> ExecuteAsync(CommandContext context, UpdateSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var buildDate = updateService.BuildDate;
            ConsoleServices.Output.WriteInfo(ConsoleServices.T("Current build: {0}", buildDate?.ToString("yyyy-MM-dd") ?? "unknown"));
            var check = await updateService.CheckAsync(cancellationToken);

            if (!check.HasUpdate)
            {
                if (check.Reason is not null)
                    ConsoleServices.Output.WriteWarning(ConsoleServices.T(check.Reason));
                else
                    ConsoleServices.Output.WriteSuccess(ConsoleServices.T("You're on the latest version 🎉"));
                return 0;
            }

            var release = check.Latest!;
            ConsoleServices.Output.WriteInfo(ConsoleServices.T("New version available: {0} ({1})", release.TagName, $"{release.PublishedAt:yyyy-MM-dd}"));
            if (!string.IsNullOrWhiteSpace(release.Body))
            {
                var excerpt = release.Body.Split('\n')
                    .Select(line => line.Trim())
                    .Where(line => line.Length > 0 && !line.StartsWith("#", StringComparison.Ordinal))
                    .Take(3);
                foreach (var line in excerpt)
                    ConsoleServices.Output.WriteInfo(ConsoleServices.T("  {0}", line));
            }

            if (settings.CheckOnly)
            {
                ConsoleServices.Output.WriteInfo(ConsoleServices.T("Check only — nothing downloaded. Run again without --check to update."));
                return 0;
            }

            var rid = GitHubUpdateService.GetRuntimeIdentifier();
            var asset = release.Assets.FirstOrDefault(a =>
                string.Equals(a.Name, settings.AssetName ?? $"Centurion-{rid}.zip", StringComparison.OrdinalIgnoreCase))
                ?? release.Assets.FirstOrDefault(a =>
                    a.Name.Contains(rid, StringComparison.OrdinalIgnoreCase) && a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

            if (asset is null)
            {
                ConsoleServices.Output.WriteError(ConsoleServices.T("No matching release asset for '{0}'. Available: {1}", rid, string.Join(", ", release.Assets.Select(a => a.Name))));
                return 1;
            }

            ConsoleServices.Output.WriteInfo(ConsoleServices.T("Downloading {0} ({1})...", asset.Name, FormatSize(asset.SizeBytes)));
            var stage = await updateService.StageAsync(check, settings.AssetName, cancellationToken);
            ConsoleServices.Output.WriteSuccess(ConsoleServices.T("Update staged: {0}", stage.PayloadDirectory));

            if (settings.Apply)
            {
                ConsoleServices.Output.WriteInfo(ConsoleServices.T("Launching the apply script — the program will close and restart automatically. 🔄"));
                Process.Start(new ProcessStartInfo(stage.ScriptPath) { UseShellExecute = true, WorkingDirectory = AppContext.BaseDirectory });
                return 0;
            }

            ConsoleServices.Output.WriteInfo(ConsoleServices.T("Run {0} to finish the update (it will close & restart this program).", stage.ScriptPath));
            return 0;
        }
        catch (OperationCanceledException)
        {
            ConsoleServices.Output.WriteWarning(ConsoleServices.T("Update cancelled."));
            return 1;
        }
        catch (Exception ex)
        {
            CliErrorPrinter.Print(logger, ex, "Update failed.");
            return ExitCodes.Failure;
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
