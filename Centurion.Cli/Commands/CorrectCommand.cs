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
/// Calibration: correct an existing subtitle file against the source audio and/or a reference script.
/// </summary>
public sealed class CorrectCommand(
    ITempDirectoryManager tempManager,
    SubtitleTrackCheckerOperator subtitleTrackCheckerOp,
    MediaSubtitleExtractor mediaSubtitleExtractor,
    SpellCheckOperator spellCheckOp,
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
    QualityReportOperator qualityReportOp,
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
            var needsAudio = strategy is CorrectionStrategy.TimelineOnly or CorrectionStrategy.Both;

            // 输入解析：位置参数可以是字幕文件，也可以是含字幕轨的媒体文件（自动提取字幕轨）
            var rawInput = settings.SubtitleFile?.FullName;
            var isMediaInput = rawInput is not null && IsMediaFile(rawInput);
            var mediaInput = isMediaInput ? rawInput : null;

            await using var tempDir = await tempManager.CreateTempDirectoryAsync("correct_");

            string subtitlePath;
            if (rawInput is null || isMediaInput)
            {
                var mediaPath = mediaInput ?? settings.AudioFile?.FullName;
                if (mediaPath is null)
                    throw new ArgumentException("A subtitle file, or a media file with subtitle tracks, is required.");

                var extracted = await mediaSubtitleExtractor.ExtractAsync(mediaPath, tempDir.Path, cancellationToken);
                if (extracted is null)
                    throw new InvalidOperationException(
                        $"No subtitle tracks found in '{mediaPath}'. Provide a subtitle file as INPUT_FILE, or use a media file that contains subtitle tracks.");
                subtitlePath = extracted;
            }
            else
            {
                subtitlePath = rawInput;
            }

            var outputPath = settings.OutputFile?.FullName ?? Path.ChangeExtension(subtitlePath, ".ass");
            var audioForTimeline = needsAudio ? settings.AudioFile?.FullName ?? mediaInput : null;
            if (needsAudio && audioForTimeline is null)
                throw new ArgumentException("--audio is required for the selected correction strategy (or pass a media file as INPUT_FILE).");

            var config = new WorkflowConfig
            {
                CommandName = "correct",
                InputFilePath = audioForTimeline ?? subtitlePath,
                SubtitleFilePath = subtitlePath,
                OutputFilePath = outputPath,
                ScriptFilePath = settings.ScriptFile?.FullName,
                CorrectStrategy = strategy,
                Language = settings.Language,
                MaxDriftMs = settings.MaxDrift,
                FuzzyThreshold = settings.FuzzyThreshold,
                HunspellDictionary = settings.HunspellDictionary,
                KaraokeMode = settings.Karaoke,
                ShowSpeakerLabels = settings.ShowSpeakerLabels,
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
            workflowContext.State.PipelineTempDirectory = tempDir.Path;

            var operators = new List<IPipelineOperator> { subtitleTrackCheckerOp, convertParseOp };
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

            if (settings.SpellCheck)
                operators.Add(spellCheckOp);

            operators.Add(reportOp);
            operators.Add(qualityReportOp);
            await pipelineExecutor.ExecuteAsync(operators, workflowContext, cancellationToken);

            var assDoc = AssSubBuilder.FromWorkflow(workflowContext).Build();
            await File.WriteAllTextAsync(outputPath, assDoc.ToString(), cancellationToken);

            // 输出富上下文 JSON（配置 + 各阶段句子 + 诊断）
            var contextPath = await WorkflowContextDumper.WriteAsync(workflowContext, "correct", outputPath, cancellationToken);
            ConsoleServices.Output.WriteSuccess(ConsoleServices.T("Correction completed: {0}", outputPath));
            ConsoleServices.Output.WriteInfo(ConsoleServices.T("Context JSON: {0}", contextPath));
            return 0;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            FailLogGate.Log(logger, ex, "Correction pipeline execution failed.");
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
        if ((strategy is CorrectionStrategy.TextOnly or CorrectionStrategy.Both) && settings.ScriptFile is null)
            throw new ArgumentException("--script is required for the selected correction strategy.");
        if (settings.FuzzyThreshold is <= 0 or >= 1)
            throw new ArgumentException("--fuzzy-threshold must be between 0 and 1.");
        if (settings.MaxDrift < 0)
            throw new ArgumentException("--max-drift must be non-negative.");
    }

    private static readonly HashSet<string> MediaFileExtensions =
    [
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".ts", ".mts", ".webm", ".flv",
        ".m2ts", ".mpeg", ".mpg", ".dv", ".rmvb", ".rm", ".asf", ".vob", ".ogv", ".mxf"
    ];

    private static bool IsMediaFile(string path) =>
        MediaFileExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());
}
