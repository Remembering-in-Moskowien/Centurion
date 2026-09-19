using Centurion.Models.Console;
using Centurion.Cli.Commands.Settings;
using Centurion.Abstractions;
using Centurion.Abstractions.Pipeline;
using Centurion.Core.Infrastructure;
using Centurion.Models.Ass;
using Centurion.Models.Workflow;
using Centurion.Core.Pipeline;
using Centurion.Core.Pipeline.Operators;
using Centurion.Core.Utils;
using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;

namespace Centurion.Cli.Commands;

/// <summary>
/// Calibration: correct an existing subtitle file against the source audio and/or a reference script.
/// </summary>
public sealed class CorrectCommand(
    ITempDirectoryManager tempManager,
    ConvertParseOperator convertParseOp,
    FFmpegConvertOperator ffmpegOp,
    AudioPreprocessOperator audioPreprocessOp,
    VocalSeparationOperator vocalSepOp,
    ScriptLoaderOperator scriptLoaderOp,
    SubtitleTextCorrectorOperator textCorrectorOp,
    DiarizationOperator diarizationOp,
    AlignmentOperator alignmentOp,
    OverlapResolutionOperator overlapOp,
    CorrectionReportOperator reportOp,
    PipelineExecutor pipelineExecutor,
    ILogger<CorrectCommand> logger) : AsyncCommand<CorrectSettings>
{
    /// <summary>
    /// 执行校正：按所选策略组装并运行校正管道，写出校正后的 ASS 字幕。
    /// </summary>
    /// <param name="context">Spectre 命令上下文。</param>
    /// <param name="settings">校正命令选项。</param>
    /// <param name="cancellationToken">取消令牌。</param>
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
                Language = settings.Language,
                MaxDriftMs = settings.MaxDrift,
                FuzzyThreshold = settings.FuzzyThreshold,
                KaraokeMode = settings.Karaoke,
                AudioPreprocess = new AudioPreprocessConfig
                {
                    EnableResampling = !settings.DisableAudioResampling,
                    EnableHighPass = !settings.DisableAudioHighPass,
                    EnableLoudnessNormalization = !settings.DisableAudioLoudness
                },
                VocalSeparation = settings.VocalSeparation,
                VocalSeparationModel = settings.VocalSeparationModel,
                Device = settings.Device,
            };

            var workflowContext = new SubtitleWorkflowContext(config);
            await using var tempDir = await tempManager.CreateTempDirectoryAsync("correct_");
            workflowContext.State.PipelineTempDirectory = tempDir.Path;

            var operators = new List<IPipelineOperator> { convertParseOp };
            if (strategy is CorrectionStrategy.TextOnly or CorrectionStrategy.Both)
            {
                operators.Add(scriptLoaderOp);
                operators.Add(textCorrectorOp);
            }

            if (needsAudio)
            {
                operators.Add(ffmpegOp);
                operators.Add(audioPreprocessOp);
                operators.Add(vocalSepOp);
                operators.Add(diarizationOp);
                operators.Add(alignmentOp);
                operators.Add(overlapOp);
            }

            operators.Add(reportOp);
            await pipelineExecutor.ExecuteAsync(operators, workflowContext, cancellationToken);

            var assDoc = AssSubBuilder.FromWorkflow(workflowContext).Build();
            await File.WriteAllTextAsync(outputPath, assDoc.ToString(), cancellationToken);

            // 输出富上下文 JSON（配置 + 各阶段句子 + 诊断）
            var contextPath = await WorkflowContextDumper.WriteAsync(workflowContext, "correct", outputPath, cancellationToken);
            ConsoleServices.Output.WriteMarkupLine($"[green]Correction completed: {outputPath}[/]");
            ConsoleServices.Output.WriteMarkupLine($"[grey]Context JSON: {contextPath}[/]");
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
