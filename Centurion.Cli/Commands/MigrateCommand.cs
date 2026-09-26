using Centurion.Core.Utils.Serialization;
using Centurion.Abstractions;
using Centurion.Models.Console;
using Centurion.Models.Schema;
using Spectre.Console.Cli;
using Centurion.Cli;

namespace Centurion.Cli.Commands;

/// <summary>migrate 命令的选项。</summary>
public sealed class MigrateSettings : CommandSettings
{
    /// <summary>待迁移的中间文件路径。</summary>
    [CommandArgument(0, "<file>")]
    public string File { get; set; } = string.Empty;

    /// <summary>目标版本（当前仅 "1.0"）。</summary>
    [CommandOption("--to <version>")]
    public string ToVersion { get; set; } = CenturionSchema.CurrentVersion;

    /// <summary>输出路径；缺省时原地覆盖原文件。</summary>
    [CommandOption("-o|--output <path>")]
    public string? OutputFile { get; set; }
}

/// <summary>
/// <c>migrate</c> 命令：把旧版 IR（meta/config/state）显式升级到当前 schema 版本，
/// 使文件带上 schemaVersion/generator/provenance 契约字段。已是目标版本时原样重写。
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
