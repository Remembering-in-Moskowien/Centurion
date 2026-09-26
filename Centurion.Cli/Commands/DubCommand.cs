using Centurion.Cli.Commands.Settings;
using Centurion.Abstractions;
using Centurion.Abstractions.Pipeline;
using Centurion.Abstractions.Utils;
using Centurion.Core.Capabilities.Infrastructure;using Centurion.Core.Workflow.Pipeline;using Centurion.Core.Workflow.Pipeline.Operators;using Centurion.Models.Workflow;
using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;
using Centurion.Core.Utils.Serialization;
using Centurion.Models.Console;
namespace Centurion.Cli.Commands;

/// <summary>
/// <c>dub</c> 命令：媒体译制——输入 Centurion 中间文件（含句子/翻译/说话人），输出配音后的 wav 音频与中间文件。
/// Phase 1 MVP 管线：说话人画像 → TTS 合成（Qwen3-TTS via llama-tts）→ 时间对齐 → 混音 → 质量报告。
/// 工具与模型按需自动下载（llama.cpp / Qwen3-TTS GGUF）。
/// </summary>
public sealed class DubCommand(
    ITempDirectoryManager tempManager,
    SpeakerProfilingOperator speakerProfilingOp,
    TtsSynthesisOperator ttsSynthesisOp,
    TimeAlignmentOperator timeAlignmentOp,
    AudioMixOperator audioMixOp,
    QualityReportOperator qualityReportOp,
    PipelineExecutor pipelineExecutor,
    IServiceProvider serviceProvider,
    ILogger<DubCommand> logger, ICenturionDocumentStore store) : AsyncCommand<DubSettings>
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
            var inputPath = settings.CenturionFile.FullName;
            if (!File.Exists(inputPath))
                throw new FileNotFoundException($"Centurion intermediate file not found: {inputPath}", inputPath);

            var outputPath = settings.OutputFile?.FullName
                ?? CenturionFileIO.DefaultOutputPath(inputPath, "dub");
            var wavPath = Path.ChangeExtension(inputPath, ".dub.wav");
            var mediaPath = settings.MediaFile?.FullName;
            if (!string.IsNullOrWhiteSpace(mediaPath) && !File.Exists(mediaPath))
                throw new FileNotFoundException($"Media file not found: {mediaPath}", mediaPath);

            // 加载中间文件（含句子、翻译、说话人信息）
            var loadedDoc = await store.LoadAsync(inputPath, ct);
            var workflowContext = new SubtitleWorkflowContext(loadedDoc.Config) { State = loadedDoc.State };

            // 更新配置：dub 相关字段
            var previous = workflowContext.Config;
            workflowContext.Config = new WorkflowConfig
            {
                CommandName = "dub",
                InputFilePath = mediaPath ?? previous.InputFilePath ?? inputPath,
                SubtitleFilePath = previous.SubtitleFilePath ?? inputPath,
                OutputFilePath = outputPath,
                TtsEngine = settings.TtsEngine,
                TtsModel = settings.TtsModel,
                TtsLanguage = settings.TargetLanguage,
                SpeakerReferenceDir = settings.SpeakerReference?.FullName,
                DubStrictTiming = settings.StrictTiming,
                DubBackgroundPath = settings.BackgroundFile?.FullName,
                DubLoudnessTarget = settings.LoudnessTarget,
                TtsParallelism = Math.Max(1, settings.TtsParallelism),
                DubMaxChunkSeconds = Math.Max(5, settings.MaxChunkSeconds),
                DubDucking = !settings.NoDucking,
                ShowSpeakerLabels = previous.ShowSpeakerLabels,
                Language = previous.Language,
                CacheDirectory = previous.CacheDirectory ?? "./cache"
            };

            await using var tempDir = await tempManager.CreateTempDirectoryAsync("dub_");
            workflowContext.State.PipelineTempDirectory = tempDir.Path;
            workflowContext.State.DubOutputWavPath = wavPath;

            // 说话人参考目录是用户输入，保留在临时目录之外
            // dub DAG：画像 → 合成 → 对齐 → 混音 → 质量报告（pipeline-graph 命令共享同一装配）
            var dag = BuildDubDag(speakerProfilingOp, ttsSynthesisOp, timeAlignmentOp, audioMixOp, qualityReportOp);

            // --dry-run：预览 DAG / 模型 / 成本，不执行
            if (settings.DryRun)
                return await DryRunHelper.PreviewAsync(dag, workflowContext.Config, serviceProvider, settings.Json, ct);

            await pipelineExecutor.ExecuteAsync(dag, workflowContext, ct);

            var segments = workflowContext.State.DubSegments;
            var dubbed = segments.Count(s => !s.Skipped);

            // 保存含译制分段的中间文件 + 写出译制 wav
            var outDoc = CenturionDocumentBuilder.Create(workflowContext, "dub", outputPath);
            await store.SaveAsync(outDoc, outputPath, ct);
            if (segments.Count > 0)
            {
                var mixerOutput = workflowContext.State.DubOutputWavPath;
                var finalWav = mixerOutput ?? wavPath;
                if (File.Exists(finalWav) && !string.Equals(finalWav, wavPath, StringComparison.OrdinalIgnoreCase))
                    File.Copy(finalWav, wavPath, true);
            }

            ConsoleServices.Output.WriteSuccess(ConsoleServices.T("Dubbed audio completed: {0}", wavPath));
            if (settings.Json)
            {
                JsonOutput.Write(new
                {
                    command = "dub",
                    status = "ok",
                    input = inputPath,
                    output = wavPath,
                    steps = workflowContext.State.StepTimings?.Select(kv => new { name = kv.Key, elapsedSeconds = kv.Value.TotalSeconds })
                });
            }            ConsoleServices.Output.WriteInfo(ConsoleServices.T("Synthesized {0}/{1} segments -> {2}", dubbed, segments.Count, settings.TargetLanguage));
            ConsoleServices.Output.WriteInfo(ConsoleServices.T("Intermediate file: {0}", outputPath));

            return ExitCodes.Success;
        }
        catch (Exception ex)
        {
            CliErrorPrinter.Print(logger, ex, "Dubbing pipeline execution failed.");
            return ExitCodes.Failure;
        }
    }

    /// <summary>
    /// 组装 dub DAG（pipeline graph 命令与 dub 命令共享的单一事实源）：
    /// 说话人画像 → TTS 合成 → 时间对齐 → 混音 → 质量报告。
    /// </summary>
    internal static PipelineDag BuildDubDag(
        SpeakerProfilingOperator speakerProfilingOp,
        TtsSynthesisOperator ttsSynthesisOp,
        TimeAlignmentOperator timeAlignmentOp,
        AudioMixOperator audioMixOp,
        QualityReportOperator qualityReportOp)
    {
        var builder = PipelineDag.CreateBuilder();
        builder
            .Add("Speaker Profiling", speakerProfilingOp, description: "说话人画像提取")
            .Add("TTS Synthesis", ttsSynthesisOp, dependsOn: ["Speaker Profiling"], description: "逐段 TTS 合成（Qwen3-TTS）")
            .Add("Time Alignment", timeAlignmentOp, dependsOn: ["TTS Synthesis"], description: "合成音频与字幕时间轴对齐")
            .Add("Audio Mix", audioMixOp, dependsOn: ["Time Alignment"], description: "混音/响度/ducking")
            .Add("Quality Report", qualityReportOp, dependsOn: ["Audio Mix"], description: "质量报告收尾");
        return builder.Build();
    }
}
