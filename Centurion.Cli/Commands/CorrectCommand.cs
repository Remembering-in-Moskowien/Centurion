using Centurion.Cli.Commands.Settings;
using Centurion.Core;
using Centurion.Core.Abstractions;
using Centurion.Core.Abstractions.Pipeline;
using Centurion.Core.Models;
using Centurion.Core.PipeLine;
using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;

namespace Centurion.Cli.Commands;

public sealed class CorrectCommand(
    ITempDirectoryManager tempManager,
    ConvertParseOp convertParseOp,
    FFmpegConvertOperator ffmpegOp,
    AudioPreprocessOperator audioPreprocessOp,
    TranscribeOp transcribeOp,
    ScriptLoaderOp scriptLoaderOp,
    TextPreprocessingOp textCleaningOp,
    SubtitleTimelineCorrectorOp timelineCorrectorOp,
    SubtitleTextCorrectorOp textCorrectorOp,
    AlignmentOp alignmentOp,
    CorrectionReportOp reportOp,
    PipelineExecutor pipelineExecutor,
    ILogger<CorrectCommand> logger) : AsyncCommand<CorrectSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, CorrectSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var strategy = ParseStrategy(settings.Strategy);
            Validate(settings, strategy);
            var subtitlePath = settings.SubtitleFile.FullName;
            var outputPath = settings.OutputFile?.FullName ?? Path.ChangeExtension(subtitlePath, ".ass");
            var needsAudio = strategy is CorrectionStrategy.TimelineOnly or CorrectionStrategy.Both;

            var config = new WorkflowConfig
            {
                InputFilePath = needsAudio ? settings.AudioFile!.FullName : subtitlePath,
                SubtitleFilePath = subtitlePath,
                OutputFilePath = outputPath,
                ScriptFilePath = settings.ScriptFile?.FullName,
                CorrectStrategy = strategy,
                MaxDriftMs = settings.MaxDrift,
                FuzzyThreshold = settings.FuzzyThreshold,
                EnableAlignment = settings.Align,
                KaraokeMode = settings.Karaoke,
                TranscriberEngine = settings.Transcriber,
                TranscriberModel = settings.TranscriberModel,
                InitialPrompt = settings.TranscriberPrompt,
                AudioPreprocess = new AudioPreprocessConfig
                {
                    EnableResampling = !settings.DisableAudioResampling,
                    EnableHighPass = !settings.DisableAudioHighPass,
                    EnableLoudnessNormalization = !settings.DisableAudioLoudness
                }
            };

            var workflowContext = new SubtitleWorkflowContext(config);
            await using var tempDir = await tempManager.CreateTempDirectoryAsync("correct_");
            workflowContext.State.PipelineTempDirectory = tempDir.Path;

            var operators = new List<IPipelineOperator> { convertParseOp };
            if (needsAudio)
            {
                operators.Add(ffmpegOp);
                operators.Add(audioPreprocessOp);
                operators.Add(transcribeOp);
                operators.Add(timelineCorrectorOp);
                if (settings.Align)
                    operators.Add(alignmentOp);
            }

            if (strategy is CorrectionStrategy.TextOnly or CorrectionStrategy.Both)
            {
                operators.Add(scriptLoaderOp);
                operators.Add(textCleaningOp);
                operators.Add(textCorrectorOp);
            }

            operators.Add(reportOp);
            await pipelineExecutor.ExecuteAsync(operators, workflowContext, cancellationToken);

            var assDoc = AssSubBuilder.FromWorkflow(workflowContext).Build();
            await File.WriteAllTextAsync(outputPath, assDoc.ToString(), cancellationToken);
            ConsoleServices.Output.WriteMarkupLine($"[green]Correction completed: {outputPath}[/]");
            return 0;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            ConsoleServices.Output.WriteError(ex.Message);
            logger.LogError(ex, "Correction pipeline execution failed.");
            return 1;
        }
    }

    private static CorrectionStrategy ParseStrategy(string value) => value.ToLowerInvariant() switch
    {
        "timeline-only" => CorrectionStrategy.TimelineOnly,
        "text-only" => CorrectionStrategy.TextOnly,
        "both" => CorrectionStrategy.Both,
        _ => throw new ArgumentException($"Unsupported correction strategy: {value}")
    };

    private static void Validate(CorrectSettings settings, CorrectionStrategy strategy)
    {
        if ((strategy is CorrectionStrategy.TimelineOnly or CorrectionStrategy.Both) && settings.AudioFile is null)
            throw new ArgumentException("--audio is required for the selected correction strategy.");
        if ((strategy is CorrectionStrategy.TextOnly or CorrectionStrategy.Both) && settings.ScriptFile is null)
            throw new ArgumentException("--script is required for the selected correction strategy.");
        if (settings.FuzzyThreshold is <= 0 or >= 1)
            throw new ArgumentException("--fuzzy-threshold must be between 0 and 1.");
        if (settings.MaxDrift < 0)
            throw new ArgumentException("--max-drift must be non-negative.");
    }
}