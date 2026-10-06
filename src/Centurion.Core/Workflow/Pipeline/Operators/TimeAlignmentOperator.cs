using Centurion.Abstractions.Pipeline;
using Centurion.Abstractions;
using Centurion.Models.Workflow;
using Microsoft.Extensions.Logging;
using Centurion.Core.Capabilities.Managers.Runtime;
namespace Centurion.Core.Workflow.Pipeline.Operators;

/// <summary>
/// Time alignment operator (dub Phase 3): probes the synthesized duration of each segment with ffprobe,
/// compares it to the subtitle's target duration, and stretches/compresses the synthesized speech to the
/// target duration.
/// Stretch engine (WorkflowConfig.DubStretchEngine): "rubberband" (default) uses the ffmpeg
/// librubberband filter — high-quality time-stretching with pitch preserved, 0.25x~4.0x range, which
/// covers e.g. a 3 s synthesis stretched into a 6 s subtitle window without the atempo 0.5x boundary
/// clamp; "atempo" uses the legacy ffmpeg atempo filter (0.5x~2.0x). The operator probes the ffmpeg
/// build once per run and falls back to atempo when rubberband is not compiled in.
/// When the value is out of the adjustable range, the configured behavior applies: strict mode clamps
/// to the boundary and logs a Warning; lenient mode keeps the original synthesized duration.
/// After alignment, detects overlaps between adjacent subtitle windows and sets MixOffsetMs (overlap
/// compression) for later segments, warning as needed.
/// </summary>
public sealed class TimeAlignmentOperator(
    IBinaryLocator binaryLocator,
    ProcessManager processManager,
    ILogger<TimeAlignmentOperator> logger)
    : PipelineOperatorBase<TimeAlignmentOperator>(logger)
{
    private const double MinAtempo = 0.5;
    private const double MaxAtempo = 2.0;
    private const double MinRubberband = 0.25;
    private const double MaxRubberband = 4.0;

    private bool? _rubberbandProbe;

    /// <summary>Operator name.</summary>
    public override string Name => "Time Alignment";

    /// <summary>
    /// Probes the synthesized duration of each segment and aligns it to the target duration
    /// via the configured stretch engine (rubberband preferred, atempo fallback).
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

        // Resolve the effective stretch engine once per run: rubberband when requested and the
        // ffmpeg build supports it, otherwise atempo (legacy).
        var requested = context.Config.DubStretchEngine?.Trim().ToLowerInvariant() ?? "rubberband";
        var useRubberband = requested == "rubberband" && await SupportsRubberbandAsync(ffmpeg, cancellationToken);
        if (requested == "rubberband" && !useRubberband)
            LogWarning("ffmpeg build lacks the rubberband filter; falling back to atempo.");
        var minTempo = useRubberband ? MinRubberband : MinAtempo;
        var maxTempo = useRubberband ? MaxRubberband : MaxAtempo;
        LogInfo($"Time alignment stretch engine: {(useRubberband ? "rubberband" : "atempo")} ({minTempo:F2}x~{maxTempo:F2}x).");

        foreach (var segment in segments.Where(s => !s.Skipped && s.SynthesizedWavPath is not null))
        {
            var actual = await ProbeDurationAsync(ffprobe, segment.SynthesizedWavPath!, cancellationToken);
            segment.SynthesizedDurationSec = actual;
            var target = segment.TargetDurationSec;
            if (target <= 0 || actual <= 0)
                continue;

            var tempo = actual / target;
            // tempo < 1 slows the clip down (stretches it to fill the target window),
            // tempo > 1 speeds it up (compresses it).
            if (tempo >= minTempo && tempo <= maxTempo)
            {
                await ApplyTempoAsync(ffmpeg, segment, tempo, ffprobe, useRubberband, cancellationToken);
            }
            else
            {
                if (context.Config.DubStrictTiming)
                {
                    // Strict mode: clamp to the boundary to stay as close as possible to the subtitle rhythm
                    var clamped = Math.Clamp(tempo, minTempo, maxTempo);
                    await ApplyTempoAsync(ffmpeg, segment, clamped, ffprobe, useRubberband, cancellationToken);
                    segment.Note = $"Target {target:F2}s vs synthesized {actual:F2}s; tempo {tempo:F2} clamped to {clamped:F2}.";
                    LogWarning($"Sentence '{segment.Text[..Math.Min(40, segment.Text.Length)]}' tempo {tempo:F2} out of range; clamped to {clamped:F2}.");
                }
                else
                {
                    segment.AlignedDurationSec = actual;
                    segment.Note = $"Target {target:F2}s vs synthesized {actual:F2}s out of {minTempo:0.00}x-{maxTempo:0.00}x range; left unchanged.";
                    LogWarning($"Sentence '{segment.Text[..Math.Min(40, segment.Text.Length)]}' tempo {tempo:F2} out of range; kept synthesized duration.");
                }
            }
        }

        DetectOverlaps(segments);
        LogInfo("Time alignment completed.");
    }

    /// <summary>
    /// Applies the stretch engine to reach the target tempo. The rubberband filter has a small
    /// window-alignment bias (observed ~1-2% of the target), so after the first pass the output
    /// duration is re-probed and the tempo is corrected once (at most two passes, always re-stretching
    /// from the original synthesis to avoid cascading artifacts) until the error is within 50 ms.
    /// </summary>
    private async Task ApplyTempoAsync(string ffmpeg, DubSegment segment, double tempo, string ffprobe, bool useRubberband, CancellationToken ct)
    {
        var alignedPath = segment.SynthesizedWavPath! + ".aligned.wav";
        var target = segment.TargetDurationSec;
        var adjusted = tempo;

        for (var attempt = 0; attempt < 2; attempt++)
        {
            await ApplyStretchAsync(ffmpeg, segment.SynthesizedWavPath!, adjusted, alignedPath, useRubberband, ct);
            if (!File.Exists(alignedPath))
                return;
            var actual = await ProbeDurationAsync(ffprobe, alignedPath, ct);
            if (target <= 0 || actual <= 0)
                break;
            if (Math.Abs(actual - target) <= 0.05) // within 50 ms of the target window
                break;
            // Correction pass: tempo scales linearly, so tempo_new = tempo_old * actual/target.
            var corrected = adjusted * (actual / target);
            if (Math.Abs(corrected - adjusted) < 0.001 || corrected is < 0.1 or > 10)
                break;
            adjusted = corrected;
        }

        segment.SynthesizedWavPath = alignedPath;
        segment.AlignmentTempo = tempo;
        segment.AlignedDurationSec = await ProbeDurationAsync(ffprobe, alignedPath, ct);
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

    /// <summary>
    /// Applies the selected stretch filter:
    /// rubberband=tempo=&lt;t&gt;:formant=preserved (pitch preserved, formant-preserving time stretch) or
    /// atempo=&lt;t&gt; (legacy). Output is 16-bit PCM wav.
    /// </summary>
    private async Task ApplyStretchAsync(string ffmpeg, string wavPath, double tempo, string outputPath, bool useRubberband, CancellationToken ct)
    {
        var args = new List<string>
        {
            "-y", "-i", wavPath,
            "-filter:a", BuildStretchFilter(tempo, useRubberband),
            "-c:a", "pcm_s16le", outputPath
        };
        await processManager.ExecuteAsync(ffmpeg, args, ct);
    }

    /// <summary>
    /// Builds the ffmpeg audio filter string for the selected stretch engine.
    /// Rubberband uses <c>rubberband=tempo=&lt;t&gt;:formant=preserved</c> (time stretch with pitch and
    /// formants preserved); atempo uses <c>atempo=&lt;t&gt;</c>. The tempo is formatted with the
    /// invariant culture so the filter is valid on any locale.
    /// </summary>
    /// <param name="tempo">Stretch ratio (actual/target).</param>
    /// <param name="useRubberband">Whether to use the rubberband filter instead of atempo.</param>
    internal static string BuildStretchFilter(double tempo, bool useRubberband)
    {
        var tempoText = tempo.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        return useRubberband
            ? $"rubberband=tempo={tempoText}:formant=preserved"
            : $"atempo={tempoText}";
    }

    /// <summary>
    /// Probes whether the ffmpeg build includes the rubberband filter (once per run, cached):
    /// runs <c>ffmpeg -filters</c> and looks for the "rubberband" filter line.
    /// </summary>
    private async Task<bool> SupportsRubberbandAsync(string ffmpeg, CancellationToken ct)
    {
        if (_rubberbandProbe is { } cached)
            return cached;
        try
        {
            var output = await processManager.ExecuteAsync(ffmpeg, new[] { "-hide_banner", "-filters" }, ct);
            _rubberbandProbe = output.IndexOf("rubberband", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        catch
        {
            _rubberbandProbe = false;
        }
        return _rubberbandProbe.Value;
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
