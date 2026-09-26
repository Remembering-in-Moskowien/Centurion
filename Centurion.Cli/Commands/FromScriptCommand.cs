using Centurion.Cli.Commands.Settings;
using Centurion.Abstractions;
using Centurion.Abstractions.Pipeline;
using Centurion.Core.Capabilities.Infrastructure;using Centurion.Core.Workflow.Factories;using Centurion.Models.Ass;
using Centurion.Models.Workflow;
using Centurion.Core.Workflow.Pipeline;using Centurion.Core.Workflow.Pipeline.Operators;using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;
using Centurion.Abstractions.Utils;
using Centurion.Core.Utils.Serialization;
using Centurion.Models.Console;
namespace Centurion.Cli.Commands;

/// <summary>
/// Script timing: align a plain-text script to the media and generate timed subtitles.
/// </summary>
public sealed class FromScriptCommand(
    ITempDirectoryManager tempManager,
    SubtitleTrackCheckerOperator subtitleTrackCheckerOp,
    FFmpegConvertOperator ffmpegOp,
    AudioPreprocessOperator audioPreprocessOp,
    VocalSeparationOperator vocalSepOp,
    PipelineOperatorFactory operatorFactory,
    ScriptLoaderOperator scriptLoaderOp,
    TextPreprocessingOperator textCleaningOp,
    ScriptTimelineMapperOperator mapperOp,
    QualityReportOperator qualityReportOp,
    PipelineExecutor pipelineExecutor,
    ILogger<FromScriptCommand> logger, ICenturionDocumentStore store) : AsyncCommand<FromScriptSettings>
{
    /// <summary>
    /// 执行脚本对齐流程：转录、说话人分割、脚本映射与对齐，写出带时间轴的 Centurion 中间文件。
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

            var outputPath = settings.OutputFile?.FullName ?? CenturionFileIO.DefaultOutputPath(inputPath);
            var config = new WorkflowConfig
            {
                CommandName = "from-script",
                InputFilePath = inputPath,
                OutputFilePath = outputPath,
                ScriptFilePath = settings.ScriptFile.FullName,
                MapperStrategy = "rule",
                SplitStrategy = "nlp",
                Language = settings.Language,
                ShowSpeakerLabels = settings.ShowSpeakerLabels,
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
                AlignmentChunkGapSeconds = settings.AlignmentChunkGapSeconds,
                AlignmentMaxChunkSeconds = settings.AlignmentMaxChunkSeconds,
                MaxCps = settings.MaxCps,
                MaxCharsPerLine = settings.MaxCharsPerLine,
                CoverageThreshold = settings.CoverageThreshold,
                FillGapWithEllipsis = settings.FillGapWithEllipsis,
                CacheDirectory = "./cache"
            };

            var workflowContext = new SubtitleWorkflowContext(config);
            var operators = new List<IPipelineOperator>
            {
                subtitleTrackCheckerOp,
                ffmpegOp,
                audioPreprocessOp,
                vocalSepOp,
                operatorFactory.CreateTranscribeOperator(config),
                scriptLoaderOp,
                textCleaningOp,
                mapperOp,
            };
            var diarizationOperator = operatorFactory.CreateDiarizationOperator(config);
            if (diarizationOperator is not null)
                operators.Insert(5, diarizationOperator);
            var alignmentOperator = operatorFactory.CreateAlignmentOperator(config);
            if (alignmentOperator is not null)
                operators.Add(alignmentOperator);
            operators.Add(qualityReportOp);

            await using var tempDir = await tempManager.CreateTempDirectoryAsync("pipeline_");
            workflowContext.State.PipelineTempDirectory = tempDir.Path;
            await pipelineExecutor.ExecuteAsync(operators, workflowContext, ct);

            // 保存为 Centurion 中间文件（含词级时间戳/说话人/脚本映射等全部详细信息）
            var outDoc = CenturionDocumentBuilder.Create(workflowContext, "from-script", outputPath);
            await store.SaveAsync(outDoc, outputPath, ct);

            ConsoleServices.Output.WriteSuccess(ConsoleServices.T("Subtitle generation completed"));
            ConsoleServices.Output.WriteInfo(ConsoleServices.T("Build subtitles with: {0}", "Centurion build <file>.centurion.json"));
            return 0;
        }
        catch (Exception ex)
        {
            FailLogGate.Log(logger, ex, "From-script pipeline execution failed.");
            return 1;
        }
    }
}
