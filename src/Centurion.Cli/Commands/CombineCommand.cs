using Centurion.Abstractions;
using Centurion.Abstractions.Pipeline;
using Centurion.Cli.Commands.Settings;
using Centurion.Core.Utils.Serialization;
using Centurion.Core.Utils.Reporting;
using Centurion.Core.Workflow.Pipeline;
using Centurion.Core.Workflow.Pipeline.Operators;
using Centurion.Models.Ass;
using Centurion.Models.Console;
using Centurion.Models.Workflow;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;

namespace Centurion.Cli.Commands;

/// <summary>
/// <c>combine</c> command: merges multiple subtitle sources into one timeline.
/// Sources may be media files with embedded subtitle tracks or genuine subtitle files
/// such as ASS/SRT/TXT. The command emits a Centurion IR by default and may also render
/// a direct .ass/.srt/.txt file when the output extension points to a subtitle format.
/// </summary>
public sealed class CombineCommand(
    PipelineExecutor executor,
    IServiceProvider serviceProvider,
    ICenturionDocumentStore store,
    ILogger<CombineCommand> logger)
    : AsyncCommand<CombineSettings>
{
    /// <summary>
    /// Runs the combine pipeline by enumerating media subtitle tracks and/or subtitle files,
    /// then merging and deduplicating them into a single final subtitle stream.
    /// </summary>
    protected override async Task<int> ExecuteAsync(CommandContext context, CombineSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var mediaInputs = ResolveExistingFiles(settings.Inputs)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var subtitleInputs = ResolveExistingFiles(settings.SubtitleFiles)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (mediaInputs.Count == 0 && subtitleInputs.Count == 0)
                throw new ArgumentException("No media file or subtitle file was supplied. Pass one or more file paths, or add --subtitle <file>.");

            var primaryInput = mediaInputs.FirstOrDefault() ?? subtitleInputs.FirstOrDefault()
                ?? throw new InvalidOperationException("No valid input file could be resolved for the combine command.");
            var outputFormat = ResolveFormat(settings.Format, settings.OutputFile?.FullName);
            var outputPath = settings.OutputFile is not null
                ? settings.OutputFile.FullName
                : BuildDefaultOutputPath(primaryInput, outputFormat);

            var config = new WorkflowConfig
            {
                CommandName = "combine",
                InputFilePath = primaryInput,
                SubtitleFilePath = subtitleInputs.FirstOrDefault() ?? primaryInput,
                OutputFilePath = outputPath,
                Language = settings.Language
            };

            var workflowContext = new SubtitleWorkflowContext(config);
            workflowContext.State.Extensions[CombineParseOperator.MediaInputsKey] = mediaInputs.ToArray();
            workflowContext.State.Extensions[CombineParseOperator.SubtitleInputsKey] = subtitleInputs.ToArray();
            workflowContext.State.Extensions[CombineParseOperator.TrackFilterKey] = settings.Tracks.Length > 0
                ? settings.Tracks.Distinct().OrderBy(x => x).ToArray()
                : Array.Empty<int>();
            workflowContext.State.Extensions[CombineDedupeOperator.ToleranceKey] = settings.DedupeToleranceMs;
            workflowContext.State.Extensions[CombineDedupeOperator.SimilarityKey] = settings.DedupeSimilarity;

            var dag = PipelineDag.CreateBuilder()
                .Add("Combine Parse", serviceProvider.GetRequiredService<CombineParseOperator>(),
                    description: "Enumerate subtitle tracks from media and parse subtitle files into a unified source list")
                .Add("Combine Merge", serviceProvider.GetRequiredService<CombineMergeOperator>(),
                    dependsOn: ["Combine Parse"],
                    description: "Merge all subtitle sources into a single timeline")
                .Add("Combine Dedupe", serviceProvider.GetRequiredService<CombineDedupeOperator>(),
                    dependsOn: ["Combine Merge"],
                    description: "Drop near-duplicate subtitle lines within the configured timing tolerance")
                .Build();

            if (settings.DryRun)
                return await DryRunHelper.PreviewAsync(dag, config, serviceProvider, settings.Json, cancellationToken);

            await executor.ExecuteAsync(dag, workflowContext, cancellationToken);

            var ext = Path.GetExtension(outputPath).ToLowerInvariant();
            if (ext is ".srt" or ".ass" or ".ssa" or ".txt")
            {
                var rendered = outputFormat switch
                {
                    "srt" => SubtitleFormatRenderer.RenderSrt(workflowContext),
                    "txt" => SubtitleFormatRenderer.RenderTxt(workflowContext),
                    _ => AssSubBuilder.FromWorkflow(workflowContext).Build().ToString()
                };
                await File.WriteAllTextAsync(outputPath, rendered, cancellationToken);
                ConsoleServices.Output.WriteSuccess(ConsoleServices.T("Combined subtitles written to {0}", outputPath));
                if (settings.Json)
                    JsonOutput.Write(new { command = "combine", status = "ok", output = outputPath, sentences = workflowContext.State.CurrentSentences.Count });
                return ExitCodes.Success;
            }

            var doc = CenturionDocumentBuilder.Create(workflowContext, "combine", outputPath);
            await store.SaveAsync(doc, outputPath, cancellationToken);

            ConsoleServices.Output.WriteSuccess(ConsoleServices.T("Combined subtitle set saved to {0}", outputPath));
            ConsoleServices.Output.WriteInfo(ConsoleServices.T("Merged {0} sentence(s) from {1} source(s).",
                workflowContext.State.CurrentSentences.Count,
                mediaInputs.Count + subtitleInputs.Count));

            if (settings.Json)
            {
                JsonOutput.Write(new
                {
                    command = "combine",
                    status = "ok",
                    input = mediaInputs.Concat(subtitleInputs).ToArray(),
                    output = outputPath,
                    sentences = workflowContext.State.CurrentSentences.Count,
                    sources = mediaInputs.Count + subtitleInputs.Count
                });
            }

            return ExitCodes.Success;
        }
        catch (Exception ex)
        {
            CliErrorPrinter.Print(logger, ex, "Combine pipeline execution failed.");
            return ExitCodes.Failure;
        }
    }

    private static List<string> ResolveExistingFiles(IEnumerable<string> values)
    {
        return values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => Path.GetFullPath(v))
            .Where(File.Exists)
            .ToList();
    }

    private static string BuildDefaultOutputPath(string? primaryInput, string outputFormat)
    {
        var baseDir = string.IsNullOrWhiteSpace(primaryInput)
            ? Directory.GetCurrentDirectory()
            : Path.GetDirectoryName(primaryInput) ?? Directory.GetCurrentDirectory();

        var baseName = string.IsNullOrWhiteSpace(primaryInput)
            ? "combined"
            : Path.GetFileNameWithoutExtension(primaryInput);

        var extension = outputFormat switch
        {
            "srt" => ".srt",
            "txt" => ".txt",
            "ass" => ".ass",
            _ => ".centurion.json"
        };

        return Path.Combine(baseDir, $"{baseName}.combined{extension}");
    }

    private static string ResolveFormat(string? format, string? outputPath)
    {
        if (!string.IsNullOrWhiteSpace(format))
        {
            var normalized = format.Trim().ToLowerInvariant();
            return normalized switch
            {
                "srt" => "srt",
                "txt" => "txt",
                "ass" or "ssa" => "ass",
                _ => throw new ArgumentException($"Unsupported combine output format '{format}'. Use ass, srt or txt.")
            };
        }

        var ext = outputPath is null ? string.Empty : Path.GetExtension(outputPath).ToLowerInvariant();
        return ext switch
        {
            ".srt" => "srt",
            ".txt" => "txt",
            ".ass" or ".ssa" => "ass",
            _ => "centurion"
        };
    }
}
