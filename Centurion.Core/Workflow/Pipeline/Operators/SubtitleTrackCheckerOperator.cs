using System.Text.Json;
using Centurion.Abstractions.Pipeline;
using Centurion.Core.Capabilities.Infrastructure;using Centurion.Models.Workflow;
using Microsoft.Extensions.Logging;
using Centurion.Core.Utils.Media;
using Centurion.Models.Console;
namespace Centurion.Core.Workflow.Pipeline.Operators;

/// <summary>
/// 字幕轨检查算子：在生成/校准/打轴管道的最前端运行，
/// 使用 mkvtoolnix（mkvmerge -i）探测输入媒体中是否已存在字幕轨，
/// 有则警告用户，并将检查报告写入输出目录（{输出}.tracks.json）。
/// 检查失败或工具缺失时不阻断管道，仅记录警告。
/// </summary>
public sealed class SubtitleTrackCheckerOperator(
    MkvToolNixChecker checker,
    ILogger<SubtitleTrackCheckerOperator> logger)
    : PipelineOperatorBase<SubtitleTrackCheckerOperator>(logger)
{
    /// <summary>算子名称。</summary>
    public override string Name => "Subtitle Track Check";

    /// <summary>
    /// 执行检查：读取输入媒体 → mkvmerge 探测轨道 → 警告既有字幕 → 报告落盘。
    /// </summary>
    /// <param name="context">工作流上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public override async Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken)
    {
        var input = context.Config.InputFilePath;
        if (string.IsNullOrWhiteSpace(input) || !File.Exists(input))
        {
            LogInfo("No media input; subtitle track check skipped.");
            return;
        }

        var result = await checker.CheckAsync(input, cancellationToken);
        context.State.Extensions["SubtitleTrackCheck"] = result;

        if (!result.Checked)
        {
            LogWarning($"Subtitle track check skipped: {result.Message}");
            return;
        }

        // 已有字幕轨：向用户发出预警
        if (result.HasSubtitleTracks)
        {
            var summary = string.Join("; ", result.SubtitleTracks.Select(t => t.Summary));
            ConsoleServices.Output.WriteWarning(
                ConsoleServices.T("Existing subtitle track(s) found in {0}: {1}", result.SourceFile, summary));
            LogWarning($"Existing subtitle tracks in '{result.SourceFile}': {summary}");
        }
        else
        {
            ConsoleServices.Output.WriteInfo(
                ConsoleServices.T("No existing subtitle tracks in {0}.", result.SourceFile));
            LogInfo($"No subtitle tracks in '{result.SourceFile}'.");
        }

        // 报告写入输出目录（与输出字幕同目录、同名 .tracks.json）
        var outputPath = context.Config.OutputFilePath;
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            var reportPath = Path.ChangeExtension(outputPath, ".tracks.json");
            await File.WriteAllTextAsync(
                reportPath,
                JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }),
                cancellationToken);
            ConsoleServices.Output.WriteInfo(ConsoleServices.T("Subtitle track report: {0}", reportPath));
            LogInfo($"Subtitle track report written to '{reportPath}'.");
        }
    }
}
