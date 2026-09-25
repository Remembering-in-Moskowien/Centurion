using Centurion.Abstractions.Pipeline;
using Centurion.Core.Utils;
using Centurion.Abstractions;
using Centurion.Core.Managers;
using Centurion.Models.Workflow;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Pipeline.Operators;

/// <summary>
/// 时间对齐算子（dub Phase 3）：用 ffprobe 探测每段合成时长，与字幕目标时长比较，
/// 通过 FFmpeg atempo 把合成语音拉伸/压缩到目标时长（允许 0.5x~2.0x）。
/// 超出可调范围时按配置处理：严格模式钳制到边界并记录 Warning，宽松模式保留原合成时长。
/// 对齐完成后检测相邻字幕窗口重叠，为后段设置 MixOffsetMs（压叠）并告警。
/// </summary>
public sealed class TimeAlignmentOperator(
    IBinaryLocator binaryLocator,
    ProcessManager processManager,
    ILogger<TimeAlignmentOperator> logger)
    : PipelineOperatorBase<TimeAlignmentOperator>(logger)
{
    private const double MinAtempo = 0.5;
    private const double MaxAtempo = 2.0;

    /// <summary>算子名称。</summary>
    public override string Name => "Time Alignment";

    /// <summary>
    /// 逐段探测合成时长并 atempo 对齐到目标时长。
    /// </summary>
    /// <param name="context">工作流上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public override async Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken)
    {
        if (!context.State.Extensions.TryGetValue("DubSegments", out var raw) || raw is not List<DubSegment> segments)
            return;

        var ffmpeg = LocateFfmpeg();
        var ffprobe = LocateFfprobe();
        if (ffmpeg is null || ffprobe is null)
        {
            context.State.Warnings.Add("ffmpeg/ffprobe not found; time alignment skipped.");
            LogWarning("ffmpeg/ffprobe not found; time alignment skipped.");
            return;
        }

        foreach (var segment in segments.Where(s => !s.Skipped && s.SynthesizedWavPath is not null))
        {
            var actual = await ProbeDurationAsync(ffprobe, segment.SynthesizedWavPath!, cancellationToken);
            segment.SynthesizedDurationSec = actual;
            var target = segment.TargetDurationSec;
            if (target <= 0 || actual <= 0)
                continue;

            var tempo = target / actual;
            if (tempo is >= MinAtempo and <= MaxAtempo)
            {
                await ApplyTempoAsync(ffmpeg, segment, tempo, ffprobe, cancellationToken);
            }
            else
            {
                if (context.Config.DubStrictTiming)
                {
                    // 严格模式：钳制到边界，尽量贴合字幕节奏
                    var clamped = Math.Clamp(tempo, MinAtempo, MaxAtempo);
                    await ApplyTempoAsync(ffmpeg, segment, clamped, ffprobe, cancellationToken);
                    segment.Note = $"Target {target:F2}s vs synthesized {actual:F2}s; tempo {tempo:F2} clamped to {clamped:F2}.";
                    LogWarning($"Sentence '{segment.Text[..Math.Min(40, segment.Text.Length)]}' tempo {tempo:F2} out of range; clamped to {clamped:F2}.");
                }
                else
                {
                    segment.AlignedDurationSec = actual;
                    segment.Note = $"Target {target:F2}s vs synthesized {actual:F2}s out of {MinAtempo:0.0}x-{MaxAtempo:0.0}x range; left unchanged.";
                    LogWarning($"Sentence '{segment.Text[..Math.Min(40, segment.Text.Length)]}' tempo {tempo:F2} out of range; kept synthesized duration.");
                }
            }
        }

        DetectOverlaps(segments);
        LogInfo("Time alignment completed.");
    }

    /// <summary>应用 atempo 到目标 tempo，记录对齐后时长与最终 tempo。</summary>
    private async Task ApplyTempoAsync(string ffmpeg, DubSegment segment, double tempo, string ffprobe, CancellationToken ct)
    {
        var alignedPath = segment.SynthesizedWavPath! + ".aligned.wav";
        await ApplyAtempoAsync(ffmpeg, segment.SynthesizedWavPath!, tempo, alignedPath, ct);
        if (File.Exists(alignedPath))
        {
            segment.SynthesizedWavPath = alignedPath;
            segment.AlignmentTempo = tempo;
            segment.AlignedDurationSec = await ProbeDurationAsync(ffprobe, alignedPath, ct);
        }
    }

    /// <summary>
    /// 重叠检测与降级：按目标起始时间排序后，后段与前一已调整段的实际结束时间重叠时，
    /// 把后段 MixOffsetMs 设为压叠量（保持时序、压缩重叠），并记录 Warning。
    /// 该偏移在混音阶段由 AudioMixOperator 应用到 adelay。
    /// </summary>
    internal static void DetectOverlaps(List<DubSegment> segments)
    {
        var active = segments.Where(s => !s.Skipped).OrderBy(s => s.TargetStartMs).ToList();
        for (var i = 1; i < active.Count; i++)
        {
            var prev = active[i - 1];
            var current = active[i];
            var prevEnd = prev.TargetEndMs + prev.MixOffsetMs;
            if (current.TargetStartMs < prevEnd)
            {
                var overlap = prevEnd - current.TargetStartMs;
                current.MixOffsetMs = overlap;
                current.Note = (current.Note is null ? "" : current.Note + "; ") +
                    $"Overlap with previous segment by {overlap:0}ms; compressed.";
            }
        }
    }

    private async Task<double> ProbeDurationAsync(string ffprobe, string wavPath, CancellationToken ct)
    {
        var args = new[] { "-v", "error", "-show_entries", "format=duration", "-of", "default=noprint_wrappers=1:nokey=1", wavPath };
        var output = await processManager.ExecuteAsync(ffprobe, args, ct);
        return double.TryParse(output.Trim(), System.Globalization.CultureInfo.InvariantCulture, out var duration) ? duration : 0;
    }

    private async Task ApplyAtempoAsync(string ffmpeg, string wavPath, double tempo, string outputPath, CancellationToken ct)
    {
        var args = new List<string>
        {
            "-y", "-i", wavPath,
            "-filter:a", $"atempo={tempo.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}",
            "-c:a", "pcm_s16le", outputPath
        };
        await processManager.ExecuteAsync(ffmpeg, args, ct);
    }

    private string? LocateFfmpeg()
    {
        try
        {
            return binaryLocator.Locate(OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg", "tools", "ffmpeg");
        }
        catch (Centurion.Abstractions.Exceptions.BinaryNotFoundException)
        {
            return null;
        }
    }

    private string? LocateFfprobe()
    {
        try
        {
            return binaryLocator.Locate(OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe", "tools", "ffmpeg");
        }
        catch (Centurion.Abstractions.Exceptions.BinaryNotFoundException)
        {
            return null;
        }
    }
}
