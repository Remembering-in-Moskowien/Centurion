using Centurion.Cli.Commands.Settings;
using Centurion.Abstractions;
using Centurion.Models.Ass;
using Centurion.Core.Capabilities.Infrastructure;using Centurion.Abstractions.Utils;
using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;
using Centurion.Core.Utils.Reporting;
using Centurion.Core.Utils.Serialization;
using Centurion.Models.Workflow;
using Centurion.Models.Console;
namespace Centurion.Cli.Commands;

/// <summary>
/// <c>build</c> command: renders Centurion intermediate files (*.centurion.json) into
/// subtitle files. Three output formats: ASS (default; styles/bilingual/karaoke/speakers),
/// SRT (plain-text timeline), TXT (plain-text lines). The format comes from --format or
/// the -o extension; the IR is produced by spawn/from-script/correct/translate/dub/convert.
/// </summary>
public sealed class BuildCommand(ICenturionDocumentStore store, ILogger<BuildCommand> logger) : AsyncCommand<BuildSettings>
{
    /// <summary>
    /// Runs build: load the IR → render per format → write the subtitle file.
    /// </summary>
    /// <param name="context">The Spectre command context.</param>
    /// <param name="settings">The build command settings.</param>
    /// <param name="ct">The cancellation token.</param>
    protected override async Task<int> ExecuteAsync(CommandContext context, BuildSettings settings, CancellationToken ct)
    {
        try
        {
            var inputPath = settings.CenturionFile.FullName;
            if (!File.Exists(inputPath))
                throw new FileNotFoundException($"Centurion intermediate file not found: {inputPath}", inputPath);

            if (!CenturionFileIO.IsCenturionFile(inputPath))
                throw new InvalidDataException(
                    $"'{inputPath}' is not a Centurion intermediate file. Convert subtitles first: 'Centurion convert <file>'.");

            // 格式解析：--format 优先，其次 -o 扩展名，默认 ass
            var format = ResolveFormat(settings);
            var outputPath = settings.OutputFile?.FullName ?? DefaultOutputPath(inputPath, format);

            var loadedDoc = await store.LoadAsync(inputPath, ct);
            var workflowContext = new SubtitleWorkflowContext(loadedDoc.Config) { State = loadedDoc.State };

            var content = format switch
            {
                BuildFormat.Srt => SubtitleFormatRenderer.RenderSrt(workflowContext),
                BuildFormat.Txt => SubtitleFormatRenderer.RenderTxt(workflowContext),
                _ => AssSubBuilder.FromWorkflow(workflowContext).Build().ToString()
            };

            await File.WriteAllTextAsync(outputPath, content, ct);

            var sentenceCount = workflowContext.State.CurrentSentences.Count;
            ConsoleServices.Output.WriteSuccess(ConsoleServices.T("Subtitle built: {0}", outputPath));
            ConsoleServices.Output.WriteInfo(ConsoleServices.T("Rendered {0} sentences as {1} -> {2}",
                sentenceCount, format.ToString().ToLowerInvariant(), outputPath));
            return 0;
        }
        catch (Exception ex)
        {
            CliErrorPrinter.Print(logger, ex, "Build pipeline execution failed.");
            return ExitCodes.Failure;
        }
    }

    /// <summary>Resolves the target format from --format / the -o extension.</summary>
    private static BuildFormat ResolveFormat(BuildSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.Format))
        {
            return settings.Format.ToLowerInvariant() switch
            {
                "srt" => BuildFormat.Srt,
                "txt" => BuildFormat.Txt,
                "ass" or "ssa" => BuildFormat.Ass,
                _ => throw new ArgumentException(
                    $"Unsupported build format '{settings.Format}'. Use ass, srt or txt.")
            };
        }

        if (settings.OutputFile is not null)
        {
            return Path.GetExtension(settings.OutputFile.FullName).ToLowerInvariant() switch
            {
                ".srt" => BuildFormat.Srt,
                ".txt" => BuildFormat.Txt,
                ".ass" or ".ssa" => BuildFormat.Ass,
                "" => BuildFormat.Ass,
                var unknown => throw new ArgumentException(
                    $"Unsupported output extension '{unknown}'. Use .ass, .srt or .txt (or set --format).")
            };
        }

        return BuildFormat.Ass;
    }

    /// <summary>Computes the default output path per format: the IR name minus .centurion.json plus the matching extension.</summary>
    private static string DefaultOutputPath(string inputPath, BuildFormat format)
    {
        var baseName = CenturionFileIO.DefaultOutputPath(Path.GetFileName(inputPath))
            .Replace(CenturionFileIO.Extension, "", StringComparison.OrdinalIgnoreCase);
        var extension = format switch
        {
            BuildFormat.Srt => ".srt",
            BuildFormat.Txt => ".txt",
            _ => ".ass"
        };
        var dir = Path.GetDirectoryName(inputPath);
        return string.IsNullOrEmpty(dir)
            ? baseName + extension
            : Path.Combine(dir, baseName + extension);
    }
}

/// <summary>Output formats supported by the build command.</summary>
public enum BuildFormat
{
    /// <summary>ASS subtitles (default; styles/bilingual/karaoke/speakers fully supported).</summary>
    Ass,
    /// <summary>SRT subtitles (plain-text timeline).</summary>
    Srt,
    /// <summary>Plain text (one line per sentence).</summary>
    Txt
}
