using Centurion.Core.Utils.Serialization;
using Centurion.Abstractions;
using Centurion.Models.Console;
using Centurion.Models.Schema;
using Spectre.Console.Cli;
using Centurion.Cli;

namespace Centurion.Cli.Commands;

/// <summary>Options for the migrate command.</summary>
public sealed class MigrateSettings : CommandSettings
{
    /// <summary>Intermediate file path to migrate.</summary>
    [CommandArgument(0, "<file>")]
    public string File { get; set; } = string.Empty;

    /// <summary>Target version (currently only "1.0").</summary>
    [CommandOption("--to <version>")]
    public string ToVersion { get; set; } = CenturionSchema.CurrentVersion;

    /// <summary>Output path; overwrites the input in place when omitted.</summary>
    [CommandOption("-o|--output <path>")]
    public string? OutputFile { get; set; }
}

/// <summary>
/// <c>migrate</c> command: explicitly upgrades older IR (meta/config/state) to the
/// current schema version, adding the schemaVersion/generator/provenance contract
/// fields. Rewrites the file as-is when it is already at the target version.
/// </summary>
public sealed class MigrateCommand(ICenturionDocumentStore store) : AsyncCommand<MigrateSettings>
{
    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(CommandContext context, MigrateSettings settings, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(settings.File))
            {
                ConsoleServices.Output.WriteError(ConsoleServices.T("Usage: Centurion migrate <file.centurion.json> --to <version>"));
                return ExitCodes.Failure;
            }

            var document = await store.MigrateAsync(settings.File, settings.ToVersion, ct);
            var outputPath = settings.OutputFile ?? settings.File;
            await store.SaveAsync(document, outputPath, ct);

            ConsoleServices.Output.WriteSuccess(ConsoleServices.T(
                "Migrated {0} -> schema {1}", settings.File, document.SchemaVersion));
            ConsoleServices.Output.WriteInfo(ConsoleServices.T("Written to: {0}", outputPath));
            return 0;
        }
        catch (FileNotFoundException ex)
        {
            ConsoleServices.Output.WriteError(ex.Message);
            return ExitCodes.Failure;
        }
        catch (NotSupportedException ex)
        {
            ConsoleServices.Output.WriteError(ex.Message);
            return ExitCodes.Failure;
        }
        catch (InvalidDataException ex)
        {
            ConsoleServices.Output.WriteError(ex.Message);
            return ExitCodes.Failure;
        }
    }
}
