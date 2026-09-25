using Centurion.Cli.Commands.Settings;
using Centurion.Abstractions;
using Centurion.Abstractions.Pipeline;
using Centurion.Abstractions.Utils;
using Centurion.Core.Infrastructure;
using Centurion.Core.Pipeline;
using Centurion.Core.Pipeline.Operators;
using Centurion.Core.Utils;
using Centurion.Models.Workflow;
using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;

namespace Centurion.Cli.Commands;

/// <summary>
/// <c>dub</c> 命令：媒体译制——输入双语（或已翻译）字幕，输出配音后的 wav 音频。
/// Phase 1 MVP 管线：双语解析 → 说话人画像 → TTS 合成（Qwen3-TTS via llama-tts）→ 时间对齐 → 混音 → 质量报告。
/// 工具与模型按需自动下载（llama.cpp / Qwen3-TTS GGUF）。
/// </summary>
public sealed class DubCommand(
    ITempDirectoryManager tempManager,
    BilingualSubtitleParserOperator bilingualParserOp,
    SpeakerProfilingOperator speakerProfilingOp,
    TtsSynthesisOperator ttsSynthesisOp,
    TimeAlignmentOperator timeAlignmentOp,
    AudioMixOperator audioMixOp,
    QualityReportOperator qualityReportOp,
    PipelineExecutor pipelineExecutor,
    ILogger<DubCommand> logger) : AsyncCommand<DubSettings>
{
    /// <summary>
    /// 执行译制流程：组装 dub 管线并运行，输出译制 wav。
    /// </summary>
    /// <param name="context">Spectre 命令上下文。</param>
    /// <param name="settings">dub 命令选项。</param>
    /// <param name="ct">取消令牌。</param>
    protected override async Task<int> ExecuteAsync(CommandContext context, DubSettings settings, CancellationToken ct)
    {
        try
        {
            var subtitlePath = settings.SubtitleFile.FullName;
            if (!File.Exists(subtitlePath))
                throw new FileNotFoundException($"Subtitle file not found: {subtitlePath}", subtitlePath);

            var outputPath = settings.OutputFile?.FullName ?? Path.ChangeExtension(subtitlePath, ".dub.wav");
            var mediaPath = settings.MediaFile?.FullName;
            if (!string.IsNullOrWhiteSpace(mediaPath) && !File.Exists(mediaPath))
                throw new FileNotFoundException($"Media file not found: {mediaPath}", mediaPath);

            var config = new WorkflowConfig
            {
                CommandName = "dub",
                InputFilePath = mediaPath ?? subtitlePath,
                SubtitleFilePath = subtitlePath,
                OutputFilePath = outputPath,
                TranslationSubtitlePath = settings.TranslationFile?.FullName,
                TtsEngine = settings.TtsEngine,
                TtsModel = settings.TtsModel,
                TtsLanguage = settings.TargetLanguage,
                SpeakerReferenceDir = settings.SpeakerReference?.FullName,
                DubStrictTiming = settings.StrictTiming,
                DubBackgroundPath = settings.BackgroundFile?.FullName,
                DubLoudnessTarget = settings.LoudnessTarget,
                TtsParallelism = Math.Max(1, settings.TtsParallelism),
                DubMaxChunkSeconds = Math.Max(5, settings.MaxChunkSeconds),
                DubDucking = !settings.NoDucking
            };

            var workflowContext = new SubtitleWorkflowContext(config);
            await using var tempDir = await tempManager.CreateTempDirectoryAsync("dub_");
            workflowContext.State.PipelineTempDirectory = tempDir.Path;

            // 说话人参考目录是用户输入，保留在临时目录之外
            var operators = new List<IPipelineOperator>
            {
                bilingualParserOp,
                speakerProfilingOp,
                ttsSynthesisOp,
                timeAlignmentOp,
                audioMixOp,
                qualityReportOp
            };

            await pipelineExecutor.ExecuteAsync(operators, workflowContext, ct);

            var segments = workflowContext.State.Extensions.TryGetValue("DubSegments", out var rawSegments)
                ? rawSegments as List<DubSegment> ?? []
                : [];
            var dubbed = segments.Count(s => !s.Skipped);

            // 富上下文 JSON（含 DubSegments 细节）
            var contextPath = await WorkflowContextDumper.WriteAsync(workflowContext, "dub", outputPath, ct);

            ConsoleServices.Output.WriteSuccess(ConsoleServices.T("Dubbed audio completed: {0}", outputPath));
            ConsoleServices.Output.WriteInfo(ConsoleServices.T("Synthesized {0}/{1} segments -> {2}", dubbed, segments.Count, settings.TargetLanguage));
            ConsoleServices.Output.WriteInfo(ConsoleServices.T("Context JSON: {0}", contextPath));
            return 0;
        }
        catch (Exception ex)
        {
            FailLogGate.Log(logger, ex, "Dubbing pipeline execution failed.");
            return 1;
        }
    }
}
