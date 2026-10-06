using Centurion.Abstractions.Pipeline;
using Centurion.Abstractions;
using Centurion.Models;
using Centurion.Models.Workflow;
using Microsoft.Extensions.Logging;
using Centurion.Core.Capabilities.Managers.Runtime;
namespace Centurion.Core.Workflow.Pipeline.Operators;

/// <summary>
/// Speaker profiling operator (dub Phase 2): picks a reference audio clip for each speaker and writes it to State.DubSpeakerReferences.
/// Selection is upgraded to SNR-based smart picking: among candidate sentences of suitable duration (2~8s, target 4s), estimates speech RMS with ffmpeg astats,
/// computes SNR against the media's silence noise floor, and picks the segment with the highest SNR; if ffmpeg analysis fails, falls back to the longest sentence (Phase 1 behavior).
/// When <c>--speaker-reference</c> is set manually, the SPEAKER_xx.wav files in that directory are used directly.
/// </summary>
public sealed class SpeakerProfilingOperator(
    IBinaryLocator binaryLocator,
    ProcessManager processManager,
    ILogger<SpeakerProfilingOperator> logger)
    : PipelineOperatorBase<SpeakerProfilingOperator>(logger)
{
    /// <summary>Minimum reference audio duration (seconds): shorter sentences are skipped.</summary>
    internal const double MinReferenceSeconds = 2.0;

    /// <summary>Target reference audio duration (seconds): selection prefers clips close to this value.</summary>
    internal const double TargetReferenceSeconds = 4.0;

    /// <summary>Maximum reference audio duration (seconds): longer clips are truncated when cropped.</summary>
    private const double MaxReferenceSeconds = 8.0;

    /// <summary>Maximum number of candidate sentences scored per speaker by SNR.</summary>
    private const int MaxCandidatesPerSpeaker = 3;

    /// <summary>Operator name.</summary>
    public override string Name => "Speaker Profiling";

    /// <summary>
    /// Picks a reference audio clip for each speaker and writes it to State.Extensions["DubSpeakerReferences"] (Dictionary&lt;string, string&gt;).
    /// </summary>
    /// <param name="context">Workflow context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public override async Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken)
    {
        var sentences = context.State.CurrentSentences;
        var references = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // 1) A manually specified reference directory takes precedence
        var manualDir = context.Config.SpeakerReferenceDir;
        if (!string.IsNullOrWhiteSpace(manualDir) && Directory.Exists(manualDir))
        {
            foreach (var file in Directory.GetFiles(manualDir, "*.wav", SearchOption.TopDirectoryOnly))
            {
                var speaker = Path.GetFileNameWithoutExtension(file);
                references[speaker] = file;
            }
            context.State.DubSpeakerReferences = references;
            LogInfo($"Loaded {references.Count} manual speaker reference(s) from '{manualDir}'.");
            return;
        }

        // 2) Crop from the media: SNR-based smart selection
        var mediaPath = context.Config.InputFilePath;
        if (!string.IsNullOrWhiteSpace(mediaPath) && File.Exists(mediaPath) && sentences.Count > 0)
        {
            var tempDir = context.State.PipelineTempDirectory ?? throw new InvalidOperationException("Pipeline temp directory is not initialized.");
            var speakers = sentences
                .Where(s => !string.IsNullOrWhiteSpace(s.Speaker))
                .Select(s => s.Speaker!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (speakers.Count > 0)
            {
                var ffmpeg = LocateFfmpeg();
                if (ffmpeg is not null)
                {
                    var noiseFloorDb = await EstimateNoiseFloorDbAsync(ffmpeg, mediaPath, cancellationToken);
                    foreach (var speaker in speakers)
                    {
                        var best = await PickBestReferenceAsync(ffmpeg, mediaPath, sentences, speaker, noiseFloorDb, cancellationToken);
                        if (best is null)
                            continue;

                        var refPath = Path.Combine(tempDir, $"ref_{Sanitize(speaker)}.wav");
                        await CropAsync(ffmpeg, mediaPath, best.Start / 1000.0,
                            Math.Min(MaxReferenceSeconds, (best.End - best.Start) / 1000.0), refPath, cancellationToken);
                        if (File.Exists(refPath) && new FileInfo(refPath).Length > 0)
                            references[speaker] = refPath;
                    }
                    LogInfo($"Noise floor {noiseFloorDb:0.0} dB; SNR-based selection applied.");
                }
                else
                {
                    context.State.Warnings.Add("ffmpeg not found; speaker reference extraction skipped (TTS will use default voice).");
                }
            }
        }

        context.State.DubSpeakerReferences = references;
        LogInfo($"Prepared {references.Count} speaker reference(s).");
    }

    /// <summary>
    /// Scores a candidate sentence for a given speaker: the closer the duration is to <see cref="TargetReferenceSeconds"/>, the better, and the higher the SNR (speech RMS - noise floor), the better.
    /// Falls back to duration closeness when no SNR data is available.
    /// </summary>
    /// <param name="candidate">Candidate sentence.</param>
    /// <param name="noiseFloorDb">Noise floor RMS (dB); null means unavailable.</param>
    /// <param name="speechRmsDb">Speech RMS of this sentence (dB); null means analysis failed.</param>
    /// <returns>The score (higher is better).</returns>
    internal static double ScoreCandidate(Sentence candidate, double? noiseFloorDb, double? speechRmsDb)
    {
        var duration = Math.Max(0, candidate.End - candidate.Start) / 1000.0;
        var durationScore = 1.0 / (1.0 + Math.Abs(duration - TargetReferenceSeconds) / TargetReferenceSeconds);

        double snrScore;
        if (speechRmsDb is not null && noiseFloorDb is not null)
            snrScore = Math.Clamp((speechRmsDb.Value - noiseFloorDb.Value) / 20.0, 0.0, 1.0);
        else if (speechRmsDb is not null)
            snrScore = Math.Clamp((speechRmsDb.Value + 60.0) / 60.0, 0.0, 1.0);
        else
            snrScore = 0.5;

        // SNR carries more weight: clarity takes priority over a perfect duration match
        return 0.65 * snrScore + 0.35 * durationScore;
    }

    /// <summary>Estimates the media's overall noise floor (dB): takes the RMS of the first 0.6s; returns null on failure.</summary>
    private async Task<double?> EstimateNoiseFloorDbAsync(string ffmpeg, string mediaPath, CancellationToken ct)
    {
        try
        {
            var output = await processManager.ExecuteAsync(ffmpeg, new List<string>
            {
                "-hide_banner", "-ss", "0", "-t", "0.6", "-i", mediaPath,
                "-af", "astats=metadata=0,ametadata=print:key=lavfi.astats.Overall.RMS_level",
                "-f", "null", "-"
            }, ct);
            return ParseRmsDb(output);
        }
        catch (Exception ex)
        {
            Logger.LogDebug("Noise floor estimation failed: {Reason}", ex.Message);
            return null;
        }
    }

    /// <summary>Analyzes the RMS (dB) of a single audio segment.</summary>
    private async Task<double?> AnalyzeRmsDbAsync(string ffmpeg, string mediaPath, double startSeconds, double durationSeconds, CancellationToken ct)
    {
        try
        {
            var output = await processManager.ExecuteAsync(ffmpeg, new List<string>
            {
                "-hide_banner", "-ss", startSeconds.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture),
                "-t", durationSeconds.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture),
                "-i", mediaPath,
                "-af", "astats=metadata=0,ametadata=print:key=lavfi.astats.Overall.RMS_level",
                "-f", "null", "-"
            }, ct);
            return ParseRmsDb(output);
        }
        catch (Exception ex)
        {
            Logger.LogDebug("RMS analysis failed for {Start}s: {Reason}", startSeconds, ex.Message);
            return null;
        }
    }

    /// <summary>Parses the RMS dB value from ffmpeg astats/metadata output (of the form "lavfi.astats.Overall.RMS_level=-23.5dB").</summary>
    internal static double? ParseRmsDb(string ffmpegOutput)
    {
        foreach (var line in ffmpegOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var idx = line.IndexOf("RMS_level=", StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                continue;
            var value = line[(idx + "RMS_level=".Length)..].Trim().TrimEnd('d', 'B', 'b');
            if (double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var db))
                return db;
        }
        return null;
    }

    /// <summary>
    /// Picks the best reference sentence for this speaker: candidates are at most <see cref="MaxCandidatesPerSpeaker"/> sentences of 2~8s duration closest to the target duration;
    /// each is analyzed for RMS and scored via <see cref="ScoreCandidate"/>. Falls back to the longest sentence when no usable candidate exists or all analyses fail (Phase 1 behavior).
    /// </summary>
    private async Task<Sentence?> PickBestReferenceAsync(
        string ffmpeg, string mediaPath, List<Sentence> sentences, string speaker, double? noiseFloorDb, CancellationToken ct)
    {
        var candidates = sentences
            .Where(s => s.Speaker != null && s.Speaker.Equals(speaker, StringComparison.OrdinalIgnoreCase))
            .Where(s => Math.Max(0, s.End - s.Start) / 1000.0 >= MinReferenceSeconds)
            .OrderBy(s => Math.Abs((Math.Max(0, s.End - s.Start) / 1000.0) - TargetReferenceSeconds))
            .Take(MaxCandidatesPerSpeaker)
            .ToList();

        if (candidates.Count == 0)
        {
            // No suitable candidates: fall back to the longest sentence (Phase 1 semantics)
            return sentences
                .Where(s => s.Speaker != null && s.Speaker.Equals(speaker, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(s => Math.Max(0, s.End - s.Start))
                .FirstOrDefault();
        }

        var best = candidates[0];
        var bestScore = double.NegativeInfinity;
        foreach (var candidate in candidates)
        {
            var duration = Math.Max(0, candidate.End - candidate.Start) / 1000.0;
            var rms = await AnalyzeRmsDbAsync(ffmpeg, mediaPath, candidate.Start / 1000.0, Math.Min(MaxReferenceSeconds, duration), ct);
            var score = ScoreCandidate(candidate, noiseFloorDb, rms);
            Logger.LogDebug("Speaker {Speaker} candidate at {Start:F2}s: RMS {Rms:0.0} dB, score {Score:F3}", speaker, candidate.Start / 1000.0, rms, score);
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        // If the score is not clearly better than the first candidate (all unavailable), keep the longest-sentence semantics: best is already the first candidate, no extra handling needed
        return best;
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

    private async Task CropAsync(string ffmpeg, string mediaPath, double startSeconds, double durationSeconds, string outputPath, CancellationToken ct)
    {
        var args = new List<string>
        {
            "-y", "-ss", startSeconds.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture),
            "-i", mediaPath,
            "-t", durationSeconds.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture),
            "-ac", "1", "-ar", "24000", "-c:a", "pcm_s16le", outputPath
        };
        await processManager.ExecuteAsync(ffmpeg, args, ct);
    }

    private static string Sanitize(string speaker) =>
        string.Concat(speaker.Where(char.IsLetterOrDigit)).ToLowerInvariant();
}
