using Centurion.Abstractions.Pipeline;
using Centurion.Abstractions;
using Centurion.Models.Workflow;
using Microsoft.Extensions.Logging;
using Centurion.Core.Capabilities.Managers.Runtime;
namespace Centurion.Core.Workflow.Pipeline.Operators;

/// <summary>
/// Time alignment operator (dub Phase 3): probes the synthesized duration of each segment with ffprobe, compares it to the subtitle's target duration,
/// and stretches/compresses the synthesized speech to the target duration via FFmpeg atempo (0.5x~2.0x allowed).
/// When the value is out of the adjustable range, the configured behavior applies: strict mode clamps to the boundary and logs a Warning; lenient mode keeps the original synthesized duration.
/// After alignment, detects overlaps between adjacent subtitle windows and sets MixOffsetMs (overlap compression) for later segments, warning as needed.
/// </summary>
public sealed class TimeAlignmentOperator(
    IBinaryLocator binaryLocator,
    ProcessManager processManager,
    ILogger<TimeAlignmentOperator> logger)
    : PipelineOperatorBase<TimeAlignmentOperator>(logger)
{
    private const double MinAtempo = 0.5;
    private const double MaxAtempo = 2.0;

    /// <summary>Operator name.</summary>
    public override string Name => "Time Alignment";

    /// <summary>
    /// Probes the synthesized duration of each segment and aligns it to the target duration via atempo.
    /// </summary>
    /// <param name="context">Workflow context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public override async Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken)
    {
        if (context.State.DubSegments.Count == 0)
            return;

        var segments = context.State.DubSegments;

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

            var tempo = actual / target;
            // atempo ratio: <1 slows the clip down (stretches it to fill the target window),
            // >1 speeds it up (compresses it). Synthesized speech shorter than the subtitle
            // window therefore gets tempo < 1, longer speech gets tempo > 1.
            if (tempo is >= MinAtempo and <= MaxAtempo)
            {
                await ApplyTempoAsync(ffmpeg, segment, tempo, ffprobe, cancellationToken);
            }
            else
            {
                if (context.Config.DubStrictTiming)
                {
                    // Strict mode: clamp to the boundary to stay as close as possible to the subtitle rhythm
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

    /// <summary>Applies atempo to the target tempo, recording the aligned duration and the final tempo.</summary>
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
    /// Overlap detection and degradation: after sorting by target start time, when a later segment overlaps the actual end time of the previous adjusted segment,
    /// sets the later segment's MixOffsetMs to the overlap amount (preserving order, compressing the overlap) and logs a Warning.
    /// This offset is applied to adelay by AudioMixOperator during the mixing stage.
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
