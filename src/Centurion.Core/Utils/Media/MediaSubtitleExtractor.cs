using System.Text.Json;
using Centurion.Abstractions;
using Centurion.Abstractions.Exceptions;
using Centurion.Models.Workflow;
using Microsoft.Extensions.Logging;
using Centurion.Core.Capabilities.Managers.Media;
using Centurion.Core.Capabilities.Managers.Runtime;
namespace Centurion.Core.Utils.Media;

/// <summary>
/// 媒体字幕提取器：从容器文件（MKV/MP4/TS 等）中提取内封字幕轨为独立字幕文件。
/// 轨道枚举优先 ffprobe（通用），提取按容器类型选择 mkvextract（无损原样导出）
/// 或 ffmpeg（转码为标准 SRT/ASS）。工具缺失或媒体无字幕轨时返回空列表（非致命）。
/// 供 correct 命令的兜底输入与 combine 命令的多轨合并使用。
/// </summary>
public sealed class MediaSubtitleExtractor(
    IBinaryLocator binaryLocator,
    ProcessManager processManager,
    MkvToolNixChecker checker,
    MkvtoolnixManager mkvtoolnixManager,
    ILogger<MediaSubtitleExtractor> logger)
{
    /// <summary>图形字幕编码（无文本可解析，跳过）。</summary>
    private static readonly HashSet<string> GraphicCodecs =
    [
        "hdmv_pgs_subtitle", "pgssub", "dvd_subtitle", "dvb_teletext", "dvb_subtitle"
    ];

    /// <summary>
    /// 从媒体中提取第一个字幕轨到指定目录（correct 兜底输入使用的旧语义）。
    /// </summary>
    /// <param name="mediaPath">媒体文件路径（mkv/mp4/ts 等）。</param>
    /// <param name="outputDirectory">提取文件输出目录（建议使用管道临时目录）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>提取出的字幕文件路径；媒体无字幕轨或提取工具不可用时返回 null。</returns>
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
    /// 枚举媒体中的可提取字幕轨（图形字幕除外）。
    /// 优先 mkvmerge -i（MKV 系容器无损信息），失败/无结果时回退 ffprobe -show_streams。
    /// </summary>
    /// <param name="mediaPath">媒体文件路径。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>字幕轨列表（TrackId 为容器内可定位的流序号/轨道号）。</returns>
    public async Task<List<MkvTrackInfo>> ListSubtitleTracksAsync(string mediaPath, CancellationToken cancellationToken)
    {
        // 1. mkvmerge 探测（mkv 容器首选，轨道号即 mkvextract 索引）
        var check = await checker.CheckAsync(mediaPath, cancellationToken);
        if (check.Checked && check.HasSubtitleTracks)
            return check.SubtitleTracks.Where(t => !IsGraphic(t.Codec)).ToList();

        // 2. ffprobe 枚举（mp4/ts/webm 等通用容器）
        var viaProbe = await ListViaFfprobeAsync(mediaPath, cancellationToken);
        if (viaProbe.Count > 0)
            return viaProbe;

        logger.LogWarning("No extractable subtitle tracks found in '{Path}' (or tooling unavailable).", mediaPath);
        return [];
    }

    /// <summary>
    /// 提取指定字幕轨到输出目录，文件名形如 media_subtitle_track_{N}{ext}。
    /// MKV 优先 mkvextract（无损原样），其余走 ffmpeg（mov_text 等转码 SRT，ass 直通）。
    /// </summary>
    /// <param name="mediaPath">媒体文件路径。</param>
    /// <param name="track">目标轨道（来自 <see cref="ListSubtitleTracksAsync"/>）。</param>
    /// <param name="outputDirectory">提取文件输出目录。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>提取出的字幕文件路径；失败时为 null。</returns>
    public async Task<string?> ExtractTrackAsync(string mediaPath, MkvTrackInfo track, string outputDirectory, CancellationToken cancellationToken)
    {
        var extension = ExtensionForCodec(track.Codec);
        var outputPath = Path.Combine(outputDirectory, $"media_subtitle_track_{track.TrackId}{extension}");

        // MKV 系容器优先 mkvextract（无损保留原始 ASS/SSA/SRT）
        if (IsMkvContainer(mediaPath) && await TryMkvextractAsync(mediaPath, track, outputPath, cancellationToken))
            return File.Exists(outputPath) ? outputPath : null;

        return await TryFfmpegAsync(mediaPath, track, extension, outputPath, cancellationToken)
            ? outputPath
            : null;
    }

    /// <summary>编码名是否图形字幕（无文本）。</summary>
    public static bool IsGraphic(string codec) =>
        GraphicCodecs.Contains(NormalizeCodecId(codec));

    /// <summary>按字幕编码选择提取文件扩展名（PGS 等图形字幕默认 null 调用方已过滤）。</summary>
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

        // 统一映射到全局流号（ffprobe 枚举给出的是 stream index，
        // mkvmerge 给出的 Track ID 在 MKV 中与 ffmpeg 全局流序号一致）
        var map = $"0:{track.TrackId}";

        // .ass/.ssa 源必为 ASS 系编码 → copy 原样保留样式；
        // .srt 一律转码 subrip（copy 在 mov_text/变体编码与 srt muxer 间会失败）
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
                    // ffprobe 路径的 TrackId：全局流索引（ffmpeg -map 0:<i> 直接可用）
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
