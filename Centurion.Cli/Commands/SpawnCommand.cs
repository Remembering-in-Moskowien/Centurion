// File: Centurion.Cli/Commands/SpawnCommand.cs
using Centurion.Cli.Commands.Settings;
using Centurion.Core.Abstractions;
using Centurion.Core.Abstractions.Pipeline;
using Centurion.Core.Infrastructure;
using Centurion.Core.Models.Ass;
using Centurion.Core.Models.Workflow;
using Centurion.Core.Pipeline;
using Centurion.Core.Pipeline.Operators;
using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;

namespace Centurion.Cli.Commands;

public sealed class SpawnCommand(
    ITempDirectoryManager tempManager,
    FFmpegConvertOperator ffmpegOp,
    AudioPreprocessOperator audioPreprocessOp,
    TranscribeOperator transcribeOp,
    SentenceSplitOperator splitOp,
    TextPreprocessingOperator textCleaningOp,
    AlignmentOperator alignmentOp,
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
                AudioPreprocess = new AudioPreprocessConfig
                {
                    EnableResampling = !settings.DisableAudioResampling,
                    EnableHighPass = !settings.DisableAudioHighPass,
                    EnableLoudnessNormalization = !settings.DisableAudioLoudness,
                    EnableNoiseReduction = settings.EnableAudioNoiseReduction,
                    SnrThresholdDb = settings.AudioSnrThresholdDb
                },

                SplitStrategy = settings.Splitter,
                MaxSentenceLength = settings.MaxLength,
                TargetSentenceLength = settings.TargetLength,
                SpreadRange = settings.SpreadRange,
                ChunkGranularity = settings.ChunkGranularity,
                MergeGapSeconds = 1.5,
                EnablePunctuationRewrite = true,
                SplitterModel = settings.SplitterModel,
                SplitterApiKey = settings.SplitterApiKey,

                EnableAlignment = settings.EnableAlignment,
                AlignmentModel = settings.AlignmentModel
            };

            var workflowContext = new SubtitleWorkflowContext(config);

            // Create pipeline temp directory
            await using var tempDir = await tempManager.CreateTempDirectoryAsync("pipeline_");
            workflowContext.State.PipelineTempDirectory = tempDir.Path;

            // ─── Dynamically build the operator pipeline ───
            var operators = new List<IPipelineOperator>
            {
                ffmpegOp,
                audioPreprocessOp,
                transcribeOp,
                splitOp,
                textCleaningOp,
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

    private static readonly HashSet<string> MediaFileExtensions =
    [
        ".mp3", ".wma", ".wav", ".flac", ".aac", ".ogg", ".ape", ".m4a", ".mka",
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".ts", ".mts", ".webm", ".flv",
        ".m2ts", ".mpeg", ".mpg", ".dv", ".rmvb", ".rm", ".asf", ".vob", ".ogv", ".mxf"
    ];
}
