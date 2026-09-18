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

public sealed class CorrectCommand(
    ITempDirectoryManager tempManager,
    FFmpegConvertOperator ffmpegOp,
    AudioPreprocessOperator audioPreprocessOp,
    TranscribeOperator transcribeOp,
    ScriptLoaderOperator scriptLoaderOp,
    TextPreprocessingOperator textCleaningOp,
    ScriptTimelineMapperOperator mapperOp,
    AlignmentOperator alignmentOp,
    PipelineExecutor pipelineExecutor,
    ILogger<CorrectCommand> logger) : AsyncCommand<FromScriptSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, FromScriptSettings settings, CancellationToken ct)
    {
        try
        {
            var inputPath = settings.InputFile.FullName;
            if (!File.Exists(inputPath))
                throw new FileNotFoundException($"Input media file not found: {inputPath}", inputPath);
            if (!File.Exists(settings.ScriptFile.FullName))
                throw new FileNotFoundException($"Script file not found: {settings.ScriptFile.FullName}", settings.ScriptFile.FullName);

            var outputPath = settings.OutputFile?.FullName ?? Path.ChangeExtension(inputPath, ".ass");
            var config = new WorkflowConfig
            {
                InputFilePath = inputPath,
                OutputFilePath = outputPath,
                ScriptFilePath = settings.ScriptFile.FullName,
                MapperStrategy = "rule",
                SplitStrategy = "nlp",
                Language = settings.Language,
                TranscriberEngine = settings.Transcriber,
                TranscriberModel = settings.TranscriberModel,
                AudioPreprocess = new AudioPreprocessConfig
                {
                    EnableResampling = !settings.DisableAudioResampling,
                    EnableHighPass = !settings.DisableAudioHighPass,
                    EnableLoudnessNormalization = !settings.DisableAudioLoudness,
                    EnableNoiseReduction = settings.EnableAudioNoiseReduction,
                    SnrThresholdDb = settings.AudioSnrThresholdDb
                },
                EnableAlignment = settings.EnableAlignment,
                AlignmentModel = settings.AlignmentModel,
                MaxCps = settings.MaxCps,
                MaxCharsPerLine = settings.MaxCharsPerLine,
                CoverageThreshold = settings.CoverageThreshold,
                FillGapWithEllipsis = settings.FillGapWithEllipsis,
                CacheDirectory = "./cache"
            };

            var workflowContext = new SubtitleWorkflowContext(config);
            await using var tempDir = await tempManager.CreateTempDirectoryAsync("pipeline_");
            workflowContext.State.PipelineTempDirectory = tempDir.Path;

            var operators = new List<IPipelineOperator>
            {
                ffmpegOp,
                audioPreprocessOp,
                transcribeOp,
                scriptLoaderOp,
                textCleaningOp,
                mapperOp,
                alignmentOp
            };
            await pipelineExecutor.ExecuteAsync(operators, workflowContext, ct);

            var assDoc = AssSubBuilder.FromWorkflow(workflowContext).Build();
            await File.WriteAllTextAsync(outputPath, assDoc.ToString(), ct);
            ConsoleServices.Output.WriteMarkupLine($"[green]Subtitle generation completed: {outputPath}[/]");
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "From-script pipeline execution failed.");
            ConsoleServices.Output.WriteError(ex.Message);
            return 1;
        }
    }
}
