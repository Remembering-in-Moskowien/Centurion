// File: Centurion.Cli/Commands/SpawnCommand.cs
using Centurion.Cli.Commands.Settings;
using Centurion.Core;
using Centurion.Core.Abstractions;
using Centurion.Core.Models;
using Centurion.Core.PipeLine;
using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;
using Centurion.Core.Operators;

namespace Centurion.Cli.Commands;

public sealed class SpawnCommand(
    ITempDirectoryManager tempManager,
    FFmpegConvertOperator ffmpegOp,
    TranscribeOp transcribeOp,
    SentenceSplitOperator splitOp,
    AlignmentOp alignmentOp,
    PipelineExecutor pipelineExecutor,
    ILogger<SpawnCommand> logger)
    : AsyncCommand<SpawnSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, SpawnSettings settings, CancellationToken ct)
    {
        try
        {
            var inputPath = settings.InputFile.FullName;
            var outputPath = settings.OutputFile?.FullName ?? Path.ChangeExtension(inputPath, ".ass");

            // Validate media file extension
            if (!MediaFileExtensions.Contains(Path.GetExtension(inputPath).ToLowerInvariant()))
                throw new ArgumentException($"Unsupported media file type: {Path.GetExtension(inputPath)}");

            // Build workflow configuration
            var config = new WorkflowConfig
            {
                InputFilePath = inputPath,
                OutputFilePath = outputPath,
                Language = settings.Language,
                NumSpeakers = settings.NumSpeakers,
                KaraokeMode = settings.Karaoke,
                CacheDirectory = "./cache",

                TranscriberEngine = settings.Transcriber,
                TranscriberModel = settings.TranscriberModel,
                InitialPrompt = settings.InitialPrompt,

                SplitStrategy = settings.Splitter,
                MaxSentenceLength = settings.MaxLength,
                TargetSentenceLength = settings.TargetLength,
                SpreadRange = settings.SpreadRange,
                MergeGapSeconds = 1.5,
                EnablePunctuationRewrite = true,
                SplitterModel = settings.SplitterModel,
                SplitterApiKey = settings.SplitterApiKey,
            };

            var workflowContext = new SubtitleWorkflowContext(config);

            // Create pipeline temp directory
            await using var tempDir = await tempManager.CreateTempDirectoryAsync("pipeline_");
            workflowContext.State.PipelineTempDirectory = tempDir.Path;

            // ─── Dynamically build the operator pipeline ───
            var operators = new List<IPipelineOperator>
            {
                ffmpegOp,
                transcribeOp,
                splitOp,
                alignmentOp
            };

            // Execute the dynamic pipeline
            await pipelineExecutor.ExecuteAsync(operators, workflowContext, ct);

            // Generate ASS subtitle file
            ConsoleServices.Output.WriteMarkupLine("[grey]Generating ASS subtitle file...[/]");
            var assBuilder = AssSubBuilder.FromWorkflow(workflowContext);
            var assDoc = assBuilder.Build();

            await File.WriteAllTextAsync(outputPath, assDoc.ToString(), ct);

            ConsoleServices.Output.WriteMarkupLine($"[green]Subtitle generation completed: {outputPath}[/]");
            return 0;
        }
        catch (Exception ex)
        {
            ConsoleServices.Output.WriteError(ex.Message);
            logger.LogError(ex, "Pipeline execution failed.");
            return 1;
        }
    }

    private static readonly HashSet<string> MediaFileExtensions = new()
    {
        ".mp3", ".wma", ".wav", ".flac", ".aac", ".ogg", ".ape", ".m4a", ".mka",
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".ts", ".mts", ".webm", ".flv",
        ".m2ts", ".mpeg", ".mpg", ".dv", ".rmvb", ".rm", ".asf", ".vob", ".ogv", ".mxf"
    };
}