using Centurion.Abstractions.Pipeline;
using Centurion.Abstractions;
using Centurion.Models.Workflow;
using Microsoft.Extensions.Logging;
using Centurion.Core.Capabilities.Managers.Runtime;
namespace Centurion.Core.Workflow.Pipeline.Operators;

/// <summary>
/// Audio mixing operator (dub Phase 2/3): places each aligned TTS segment onto a full-length
/// audio track according to the subtitle timeline.
/// Supports three modes:
/// 1) Vocals only: amix TTS segments onto a silent base track, then loudnorm loudness normalization;
/// 2) Background + ducking: when <c>DubBackgroundPath</c> is provided, the background track is first
///    sidechain-compressed (ducked) by the vocals via sidechaincompress, then mixed with the vocals
///    (on by default; disable with <c>DubDucking=false</c>);
/// 3) Overlapping adjacent segments: stacked by the MixOffsetMs computed in the TimeAlignment stage
///    (the later segment is shifted earlier).
/// Outputs the final dubbed wav.
/// </summary>
public sealed class AudioMixOperator(
    IBinaryLocator binaryLocator,
    ProcessManager processManager,
    ILogger<AudioMixOperator> logger)
    : PipelineOperatorBase<AudioMixOperator>(logger)
{
    private const int SampleRate = 44100;

    /// <summary>The operator name.</summary>
    public override string Name => "Audio Mix";

    /// <summary>
    /// Builds a full-length base audio track, aligns each segment with adelay and mixes the output;
    /// enables ducking when a background track is present, and finally normalizes with loudnorm.
    /// </summary>
    /// <param name="context">The workflow context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override async Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken)
    {
        if (context.State.DubSegments is not { Count: > 0 } segments)
            return;

        var ffmpeg = LocateFfmpeg();
        if (ffmpeg is null)
        {
            context.State.Warnings.Add("ffmpeg not found; audio mixing skipped.");
            LogWarning("ffmpeg not found; audio mixing skipped.");
            return;
        }

        var ready = segments.Where(s => !s.Skipped && File.Exists(s.SynthesizedWavPath)).ToList();
        if (ready.Count == 0)
        {
            context.State.Warnings.Add("No synthesized segments; audio mix skipped.");
            LogWarning("No synthesized segments; audio mix skipped.");
            return;
        }

        var totalMs = Math.Max(1000, (int)Math.Ceiling(segments.Max(s => s.TargetEndMs)));
        var wavPath = context.State.DubOutputWavPath
            ?? Path.ChangeExtension(context.Config.SubtitleFilePath ?? context.Config.InputFilePath ?? context.Config.OutputFilePath, ".dub.wav");
        var outputPath = !string.IsNullOrWhiteSpace(wavPath)
            ? wavPath!
            : Path.ChangeExtension(context.Config.InputFilePath, ".dub.wav")!;
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? AppContext.BaseDirectory);

        var background = context.Config.DubBackgroundPath;
        var useDucking = context.Config.DubDucking && !string.IsNullOrWhiteSpace(background) && File.Exists(background);
        if (useDucking)
            LogInfo($"Mixing with background '{background}' + ducking.");
        else if (!string.IsNullOrWhiteSpace(background) && !File.Exists(background))
            context.State.Warnings.Add($"Background file not found: {background}; mixing vocals only.");

        await MixAsync(ffmpeg, ready, totalMs, outputPath, context.Config.DubLoudnessTarget, background, useDucking, cancellationToken);
        LogInfo($"Dubbed audio written to '{outputPath}' ({ready.Count} segments, {totalMs}ms).");
    }

    /// <summary>
    /// Performs the mix with ffmpeg filter_complex:
    /// - overlays each TTS segment at the offset (TargetStartMs + MixOffsetMs) into [vox];
    /// - when a background is present and ducking is on, [bg][vox]sidechaincompress produces [duckbg], then amix;
    /// - finally loudnorm normalizes to the target LUFS.
    /// </summary>
    private async Task MixAsync(string ffmpeg, List<DubSegment> segments, int totalMs, string outputPath,
        double loudnessTarget, string? backgroundPath, bool ducking, CancellationToken ct)
    {
        var inputs = new List<string> { "-y", "-f", "lavfi", "-i", $"anullsrc=r={SampleRate}:cl=stereo", "-t", (totalMs / 1000.0).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) };
        foreach (var segment in segments)
        {
            inputs.Add("-i");
            inputs.Add(segment.SynthesizedWavPath!);
        }
        if (backgroundPath is not null)
        {
            inputs.Add("-i");
            inputs.Add(backgroundPath);
        }

        var filters = new List<string>();
        for (var i = 0; i < segments.Count; i++)
        {
            var delay = Math.Max(0, (int)Math.Round(segments[i].TargetStartMs + segments[i].MixOffsetMs));
            filters.Add($"[{i + 1}:a]aresample={SampleRate},adelay={delay}|{delay}[a{i}]");
        }

        var vocalMixInputs = string.Join("", Enumerable.Range(0, segments.Count).Select(i => $"[a{i}]"));
        filters.Add($"{vocalMixInputs}[0:a]amix=inputs={segments.Count + 1}:normalize=0:duration=first[vox]");

        var loudnorm = $"loudnorm=I={loudnessTarget.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}:TP=-1.5:LRA=11";
        if (ducking && backgroundPath is not null)
        {
            // The background track is the trailing input: index = 1 + segments.Count.
            // [vox] is consumed by two filters (the sidechain input of sidechaincompress and the final mix);
            // ffmpeg 7 cannot resolve direct fan-out, so we explicitly split it with asplit first.
            var bgIndex = 1 + segments.Count;
            filters.Add($"[vox]asplit=2[v1][v2]");
            filters.Add($"[{bgIndex}:a]aresample={SampleRate}[bg]");
            filters.Add($"[bg][v1]sidechaincompress=threshold=0.05:ratio=8:attack=50:release=400[duckbg]");
            filters.Add($"[duckbg][v2]amix=inputs=2:normalize=0:duration=first[mix]");
            filters.Add($"[mix]{loudnorm}[out]");
        }
        else
        {
            filters.Add($"[vox]{loudnorm}[out]");
        }

        var args = inputs.Concat(["-filter_complex", string.Join(";", filters), "-map", "[out]", "-c:a", "pcm_s16le", outputPath]).ToList();
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
}
