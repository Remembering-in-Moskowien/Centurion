using System.Text.Json;
using Centurion.Abstractions;
using Centurion.Abstractions.Exceptions;
using Centurion.Models.Workflow;
using Microsoft.Extensions.Logging;
using Centurion.Core.Capabilities.Managers.Media;
using Centurion.Core.Capabilities.Managers.Runtime;
namespace Centurion.Core.Utils.Media;

/// <summary>
/// Media subtitle extractor: extracts embedded subtitle tracks from container files
/// (MKV/MP4/TS, etc.) into standalone subtitle files.
/// Track enumeration prefers ffprobe (generic); extraction picks mkvextract by container
/// type (lossless passthrough) or ffmpeg (transcode to standard SRT/ASS).
/// Returns an empty list (non-fatal) when tooling is missing or the media has no subtitle track.
/// Used by the correct command's fallback input and the combine command's multi-track merge.
/// </summary>
public sealed class MediaSubtitleExtractor(
    IBinaryLocator binaryLocator,
    ProcessManager processManager,
    MkvToolNixChecker checker,
    MkvtoolnixManager mkvtoolnixManager,
    ILogger<MediaSubtitleExtractor> logger)
{
    /// <summary>Graphic subtitle codecs (no parseable text, skipped).</summary>
    private static readonly HashSet<string> GraphicCodecs =
    [
        "hdmv_pgs_subtitle", "pgssub", "dvd_subtitle", "dvb_teletext", "dvb_subtitle"
    ];

    /// <summary>
    /// Extract the first subtitle track from the media into the given directory
    /// (legacy semantics used by the correct command's fallback input).
    /// </summary>
    /// <param name="mediaPath">Path to the media file (mkv/mp4/ts, etc.).</param>
    /// <param name="outputDirectory">Output directory for the extracted file (use the pipeline temp directory).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Path to the extracted subtitle file; null when the media has no subtitle track or extraction tooling is unavailable.</returns>
    public async Task<string?> ExtractAsync(string mediaPath, string outputDirectory, CancellationToken cancellationToken)
    {
        var tracks = await ListSubtitleTracksAsync(mediaPath, cancellationToken);
        if (tracks.Count == 0)
        {
            logger.LogWarning("No extractable subtitle tracks in '{Path}'.", mediaPath);
            return null;
        }

        return await ExtractTrackAsync(mediaPath, tracks[0], outputDirectory, cancellationToken);
    }

    /// <summary>
    /// Enumerate extractable subtitle tracks in the media (excluding graphic subtitles).
    /// Prefers mkvmerge -i (lossless info for MKV-family containers); falls back to
    /// ffprobe -show_streams on failure or when no results are found.
    /// </summary>
    /// <param name="mediaPath">Path to the media file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Subtitle track list (TrackId is the stream/track number locatable within the container).</returns>
    public async Task<List<MkvTrackInfo>> ListSubtitleTracksAsync(string mediaPath, CancellationToken cancellationToken)
    {
        // 1. mkvmerge probing (preferred for mkv containers; the track number is the mkvextract index)
        var check = await checker.CheckAsync(mediaPath, cancellationToken);
        if (check.Checked && check.HasSubtitleTracks)
            return check.SubtitleTracks.Where(t => !IsGraphic(t.Codec)).ToList();

        // 2. ffprobe enumeration (generic containers such as mp4/ts/webm)
        var viaProbe = await ListViaFfprobeAsync(mediaPath, cancellationToken);
        if (viaProbe.Count > 0)
            return viaProbe;

        logger.LogWarning("No extractable subtitle tracks found in '{Path}' (or tooling unavailable).", mediaPath);
        return [];
    }

    /// <summary>
    /// Extract the given subtitle track into the output directory; the file is named like
    /// media_subtitle_track_{N}{ext}. MKV prefers mkvextract (lossless passthrough); other
    /// containers go through ffmpeg (mov_text etc. transcoded to SRT, ASS passed through).
    /// </summary>
    /// <param name="mediaPath">Path to the media file.</param>
    /// <param name="track">Target track (from <see cref="ListSubtitleTracksAsync"/>).</param>
    /// <param name="outputDirectory">Output directory for the extracted file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Path to the extracted subtitle file; null on failure.</returns>
    public async Task<string?> ExtractTrackAsync(string mediaPath, MkvTrackInfo track, string outputDirectory, CancellationToken cancellationToken)
    {
        var extension = ExtensionForCodec(track.Codec);
        var outputPath = Path.Combine(outputDirectory, $"media_subtitle_track_{track.TrackId}{extension}");

        // MKV-family containers prefer mkvextract (losslessly keeps the original ASS/SSA/SRT)
        if (IsMkvContainer(mediaPath) && await TryMkvextractAsync(mediaPath, track, outputPath, cancellationToken))
            return File.Exists(outputPath) ? outputPath : null;

        return await TryFfmpegAsync(mediaPath, track, extension, outputPath, cancellationToken)
            ? outputPath
            : null;
    }

    /// <summary>Whether the codec name is a graphic subtitle (no text).</summary>
    public static bool IsGraphic(string codec) =>
        GraphicCodecs.Contains(NormalizeCodecId(codec));

    /// <summary>Choose the extracted file extension by subtitle codec (PGS and other graphic subtitles default to null and are filtered by the caller).</summary>
    internal static string ExtensionForCodec(string codec) => codec switch
    {
        var c when c.Contains("ASS", StringComparison.OrdinalIgnoreCase) => ".ass",
        var c when c.Contains("SSA", StringComparison.OrdinalIgnoreCase) => ".ssa",
        _ => ".srt"
    };

    private static string NormalizeCodecId(string codec)
    {
        var normalized = codec ?? string.Empty;
        if (normalized.Contains("PGS", StringComparison.OrdinalIgnoreCase))
            return "hdmv_pgs_subtitle";
        if (normalized.Contains("VobSub", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("DVD", StringComparison.OrdinalIgnoreCase))
            return "dvd_subtitle";
        return normalized.ToLowerInvariant();
    }

    private static bool IsMkvContainer(string mediaPath) =>
        Path.GetExtension(mediaPath).Equals(".mkv", StringComparison.OrdinalIgnoreCase)
        || Path.GetExtension(mediaPath).Equals(".mka", StringComparison.OrdinalIgnoreCase)
        || Path.GetExtension(mediaPath).Equals(".webm", StringComparison.OrdinalIgnoreCase);

    private async Task<bool> TryMkvextractAsync(string mediaPath, MkvTrackInfo track, string outputPath, CancellationToken ct)
    {
        string mkvextract;
        try
        {
            mkvextract = binaryLocator.Locate(OperatingSystem.IsWindows() ? "mkvextract.exe" : "mkvextract", "tools/mkvtoolnix");
        }
        catch (BinaryNotFoundException ex)
        {
            logger.LogWarning("mkvextract not found ({Name}); attempting automatic MKVToolNix download...", ex.BinaryName);
            try
            {
                await mkvtoolnixManager.EnsureInstalledAsync(ct);
                mkvextract = binaryLocator.Locate(OperatingSystem.IsWindows() ? "mkvextract.exe" : "mkvextract", "tools", "mkvtoolnix");
            }
            catch (Exception installEx)
            {
                logger.LogWarning("MKVToolNix auto-download failed: {Reason}", installEx.Message);
                return false;
            }
        }

        try
        {
            await processManager.ExecuteAsync(
                mkvextract, ["tracks", mediaPath, $"{track.TrackId}:{outputPath}"], ct);
            if (File.Exists(outputPath))
            {
                logger.LogInformation("Extracted subtitle track {TrackId} from '{Path}' -> '{Output}' (mkvextract).", track.TrackId, mediaPath, outputPath);
                return true;
            }
            logger.LogWarning("mkvextract produced no output for track {TrackId} of '{Path}'.", track.TrackId, mediaPath);
        }
        catch (Exception ex)
        {
            logger.LogWarning("mkvextract failed for track {TrackId} of '{Path}': {Reason}", track.TrackId, mediaPath, ex.Message);
        }

        return false;
    }

    private async Task<bool> TryFfmpegAsync(string mediaPath, MkvTrackInfo track, string extension, string outputPath, CancellationToken ct)
    {
        string ffmpeg;
        try
        {
            ffmpeg = binaryLocator.Locate(OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg", "tools", "ffmpeg");
        }
        catch (BinaryNotFoundException)
        {
            logger.LogWarning("ffmpeg not found; cannot extract subtitle track {TrackId} from '{Path}'.", track.TrackId, mediaPath);
            return false;
        }

        // Map to the global stream number (ffprobe enumeration gives the stream index,
        // and the Track ID from mkvmerge matches ffmpeg's global stream ordinal in MKV)
        var map = $"0:{track.TrackId}";

        // .ass/.ssa sources are always ASS-family codecs -> copy keeps the styles as-is;
        // .srt is always transcoded to subrip (copy fails between mov_text/variant codecs and the srt muxer)
        var codecArg = extension is ".ass" or ".ssa"
            ? new[] { "-c:s", "copy" }
            : new[] { "-c:s", "subrip" };

        var args = new List<string> { "-y", "-loglevel", "error", "-i", mediaPath, "-map", map };
        args.AddRange(codecArg);
        args.Add(outputPath);
        try
        {
            await processManager.ExecuteAsync(ffmpeg, args, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning("ffmpeg subtitle extraction failed for '{Path}' (map {Map}): {Reason}", mediaPath, map, ex.Message);
            return false;
        }

        if (!File.Exists(outputPath))
        {
            logger.LogWarning("ffmpeg produced no subtitle output for '{Path}' (map {Map}).", mediaPath, map);
            return false;
        }

        logger.LogInformation("Extracted subtitle track {TrackId} from '{Path}' -> '{Output}' (ffmpeg).", track.TrackId, mediaPath, outputPath);
        return true;
    }

    private async Task<List<MkvTrackInfo>> ListViaFfprobeAsync(string mediaPath, CancellationToken cancellationToken)
    {
        string ffprobe;
        try
        {
            ffprobe = binaryLocator.Locate(OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe", "tools", "ffmpeg");
        }
        catch (BinaryNotFoundException)
        {
            logger.LogWarning("ffprobe not found; cannot enumerate subtitle streams in '{Path}'.", mediaPath);
            return [];
        }

        string output;
        try
        {
            output = await processManager.ExecuteAsync(
                ffprobe,
                ["-v", "error", "-print_format", "json", "-show_streams", "-select_streams", "s", mediaPath],
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning("ffprobe failed for '{Path}': {Reason}", mediaPath, ex.Message);
            return [];
        }

        var tracks = new List<MkvTrackInfo>();
        try
        {
            using var doc = JsonDocument.Parse(output);
            if (!doc.RootElement.TryGetProperty("streams", out var streams))
                return [];

            var subtitleOrdinal = 0;
            foreach (var stream in streams.EnumerateArray())
            {
                var codec = stream.TryGetProperty("codec_name", out var c) ? c.GetString() ?? "" : "";
                var type = stream.TryGetProperty("codec_type", out var t) ? t.GetString() ?? "" : "";
                if (!type.Equals("subtitle", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (GraphicCodecs.Contains(codec.ToLowerInvariant()))
                {
                    logger.LogInformation("Skipping graphic subtitle stream {Index} ({Codec}) in '{Path}' (no parseable text).",
                        stream.TryGetProperty("index", out var idx) ? idx.GetInt32() : -1, codec, mediaPath);
                    continue;
                }

                string? language = null;
                string? title = null;
                if (stream.TryGetProperty("tags", out var tags))
                {
                    if (tags.TryGetProperty("language", out var lang)) language = lang.GetString();
                    if (tags.TryGetProperty("title", out var ttl)) title = ttl.GetString();
                }

                tracks.Add(new MkvTrackInfo
                {
                    // TrackId for the ffprobe path: the global stream index (usable directly as ffmpeg -map 0:<i>)
                    TrackId = stream.TryGetProperty("index", out var i) ? i.GetInt32() : subtitleOrdinal,
                    Type = "subtitles",
                    Codec = codec,
                    Language = language,
                    Name = title
                });
                subtitleOrdinal++;
            }
        }
        catch (JsonException ex)
        {
            logger.LogWarning("ffprobe JSON unparseable for '{Path}': {Reason}", mediaPath, ex.Message);
            return [];
        }

        return tracks;
    }
}
