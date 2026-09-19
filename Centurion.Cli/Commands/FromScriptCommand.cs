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
/// Script timing: align a plain-text script to the media and generate timed subtitles.
/// </summary>
public sealed class FromScriptCommand(
    ITempDirectoryManager tempManager,
    FFmpegConvertOperator ffmpegOp,
    AudioPreprocessOperator audioPreprocessOp,
    VocalSeparationOperator vocalSepOp,
    TranscribeOperator transcribeOp,
    DiarizationOperator diarizationOp,
    ScriptLoaderOperator scriptLoaderOp,
    TextPreprocessingOperator textCleaningOp,
    ScriptTimelineMapperOperator mapperOp,
    AlignmentOperator alignmentOp,
    PipelineExecutor pipelineExecutor,
    ILogger<FromScriptCommand> logger) : AsyncCommand<FromScriptSettings>
{
    /// <summary>
    /// 执行脚本对齐流程：转录、说话人分割、脚本映射与对齐，写出带时间轴的 ASS 字幕。
    /// </summary>
    /// <param name="context">Spectre 命令上下文。</param>
    /// <param name="settings">脚本对齐命令选项。</param>
    /// <param name="ct">取消令牌。</param>
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
                VocalSeparation = settings.VocalSeparation,
                VocalSeparationModel = settings.VocalSeparationModel,
                Device = settings.Device,
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
                vocalSepOp,
                transcribeOp,
                diarizationOp,
                scriptLoaderOp,
                textCleaningOp,
                mapperOp,
                alignmentOp
            };
            await pipelineExecutor.ExecuteAsync(operators, workflowContext, ct);

            var assDoc = AssSubBuilder.FromWorkflow(workflowContext).Build();
            await File.WriteAllTextAsync(outputPath, assDoc.ToString(), ct);

            // 输出富上下文 JSON（配置 + 各阶段句子 + 诊断）
            var contextPath = await WorkflowContextDumper.WriteAsync(workflowContext, "from-script", outputPath, ct);
            ConsoleServices.Output.WriteMarkupLine($"[green]Subtitle generation completed: {outputPath}[/]");
            ConsoleServices.Output.WriteMarkupLine($"[grey]Context JSON: {contextPath}[/]");
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
