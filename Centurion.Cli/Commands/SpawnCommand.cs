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
using Centurion.Abstractions.Utils;

namespace Centurion.Cli.Commands;

/// <summary>
/// <c>spawn</c> 命令：从音视频媒体自动转录、说话人分割、分句与对齐，生成字幕。
/// </summary>
public sealed class SpawnCommand(
    ITempDirectoryManager tempManager,
    SubtitleTrackCheckerOperator subtitleTrackCheckerOp,
    FFmpegConvertOperator ffmpegOp,
    AudioPreprocessOperator audioPreprocessOp,
    VocalSeparationOperator vocalSepOp,
    TranscribeOperator transcribeOp,
    DiarizationOperator diarizationOp,
    SentenceSplitOperator splitOp,
    TextPreprocessingOperator textCleaningOp,
    AlignmentOperator alignmentOp,
    QualityReportOperator qualityReportOp,
    PipelineExecutor pipelineExecutor,
    ILogger<SpawnCommand> logger)
    : AsyncCommand<SpawnSettings>
{
    /// <summary>
    /// 执行自动字幕生成流程：组装并运行管道，写出 ASS 字幕文件。
    /// </summary>
    /// <param name="context">Spectre 命令上下文。</param>
    /// <param name="settings">spawn 命令选项。</param>
    /// <param name="ct">取消令牌。</param>
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
                CommandName = "spawn",
                InputFilePath = inputPath,
                OutputFilePath = outputPath,
                Language = settings.Language,
                NumSpeakers = settings.NumSpeakers,
                DiarizationBackend = settings.DiarizationBackend is not null
                    ? settings.DiarizationBackend
                    : settings.Diarize ? "crispasr" : "none",
                KaraokeMode = settings.Karaoke,
                ShowSpeakerLabels = settings.ShowSpeakerLabels,
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
                VocalSeparation = settings.VocalSeparation,
                VocalSeparationModel = settings.VocalSeparationModel,
                Device = settings.Device,

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
                AlignmentModel = settings.AlignmentModel,
                AlignmentChunkGapSeconds = settings.AlignmentChunkGapSeconds,
                AlignmentMaxChunkSeconds = settings.AlignmentMaxChunkSeconds
            };

            var workflowContext = new SubtitleWorkflowContext(config);

            // Create pipeline temp directory
            await using var tempDir = await tempManager.CreateTempDirectoryAsync("pipeline_");
            workflowContext.State.PipelineTempDirectory = tempDir.Path;

            // ─── Dynamically build the operator pipeline ───
            var operators = new List<IPipelineOperator>
            {
                subtitleTrackCheckerOp,
                ffmpegOp,
                audioPreprocessOp,
                vocalSepOp,
                transcribeOp,
                diarizationOp,
                splitOp,
                textCleaningOp,
                alignmentOp,
                qualityReportOp
            };

            // Execute the dynamic pipeline
            await pipelineExecutor.ExecuteAsync(operators, workflowContext, ct);

            // Generate ASS subtitle file
            ConsoleServices.Output.WriteInfo(ConsoleServices.T("Generating ASS subtitle file..."));
            var assBuilder = AssSubBuilder.FromWorkflow(workflowContext);
            var assDoc = assBuilder.Build();

            await File.WriteAllTextAsync(outputPath, assDoc.ToString(), ct);

            // 输出富上下文 JSON（配置 + 各阶段句子 + 诊断）
            var contextPath = await WorkflowContextDumper.WriteAsync(workflowContext, "spawn", outputPath, ct);

            ConsoleServices.Output.WriteSuccess(ConsoleServices.T("Subtitle generation completed: {0}", outputPath));
            ConsoleServices.Output.WriteInfo(ConsoleServices.T("Context JSON: {0}", contextPath));
            return 0;
        }
        catch (Exception ex)
        {
            FailLogGate.Log(logger, ex, "Pipeline execution failed.");
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
