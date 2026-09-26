using Centurion.Cli.Commands.Settings;
using Centurion.Core.Utils.Serialization;
using Centurion.Models.Console;
using Spectre.Console.Cli;

namespace Centurion.Cli.Commands;

/// <summary>Options for the validate command.</summary>
public sealed class ValidateSettings : GlobalCommandSettings
{
    /// <summary>Intermediate file paths to validate (multiple allowed).</summary>
    [CommandArgument(0, "<files>")]
    public string[] Files { get; set; } = [];
}

/// <summary>
/// <c>validate</c> command: checks one or more *.centurion.json files against the IR
/// contract (parseable JSON, supported schemaVersion, complete config/state structure).
/// Invalid files list every issue and exit with a non-zero code.
/// </summary>
public sealed class ValidateCommand(ICenturionDocumentStore store) : AsyncCommand<ValidateSettings>
{
    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(CommandContext context, ValidateSettings settings, CancellationToken ct)
    {
        if (settings.Files.Length == 0)
        {
            ConsoleServices.Output.WriteError(ConsoleServices.T("Usage: Centurion validate <file.centurion.json> [...]"));
            return 1;
        }

        var failed = 0;
        foreach (var file in settings.Files)
        {
            var result = await store.ValidateAsync(file, ct);
            if (result.IsValid)
            {
                ConsoleServices.Output.WriteSuccess(ConsoleServices.T("OK: {0} (schema {1})", file, result.Version ?? "unknown"));
            }
            else
            {
                failed++;
                ConsoleServices.Output.WriteError(ConsoleServices.T("INVALID: {0}", file));
                foreach (var issue in result.Issues)
                    ConsoleServices.Output.WriteError("  - " + issue);
            }
        }
        return failed == 0 ? 0 : 1;
    }
}
