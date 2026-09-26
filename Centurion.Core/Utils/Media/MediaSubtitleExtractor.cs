using Centurion.Abstractions;
using Centurion.Abstractions.Exceptions;
using Centurion.Models.Workflow;
using Microsoft.Extensions.Logging;
using Centurion.Core.Capabilities.Managers.Media;
using Centurion.Core.Capabilities.Managers.Runtime;
namespace Centurion.Core.Utils.Media;

/// <summary>
/// 媒体字幕提取器：当 correct 命令未提供字幕文件时，
/// 用 mkvtoolnix（mkvextract）从媒体中提取既有字幕轨作为输入字幕。
/// 工具缺失或无字幕轨时返回 null（由调用方决定如何处理）。
/// </summary>
public sealed class MediaSubtitleExtractor(
    IBinaryLocator binaryLocator,
    ProcessManager processManager,
    MkvToolNixChecker checker,
    MkvtoolnixManager mkvtoolnixManager,
    ILogger<MediaSubtitleExtractor> logger)
{
    /// <summary>
    /// 从媒体中提取第一个字幕轨到指定目录。
    /// </summary>
    /// <param name="mediaPath">媒体文件路径（mkv/mp4/ts 等）。</param>
    /// <param name="outputDirectory">提取文件输出目录（建议使用管道临时目录）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>提取出的字幕文件路径；媒体无字幕轨或 mkvextract 不可用时返回 null。</returns>
    public async Task<string?> ExtractAsync(string mediaPath, string outputDirectory, CancellationToken cancellationToken)
    {
        var check = await checker.CheckAsync(mediaPath, cancellationToken);
        if (!check.Checked || !check.HasSubtitleTracks)
        {
            logger.LogWarning("No extractable subtitle tracks in '{Path}'.", mediaPath);
            return null;
        }

        string mkvextract;
        try
        {
            mkvextract = binaryLocator.Locate(OperatingSystem.IsWindows() ? "mkvextract.exe" : "mkvextract", "tools/mkvtoolnix");
        }
        catch (BinaryNotFoundException ex)
        {
            logger.LogWarning("mkvextract not found ({Path}); attempting automatic MKVToolNix download...", ex.BinaryName);
            try
            {
                await mkvtoolnixManager.EnsureInstalledAsync(cancellationToken);
                mkvextract = binaryLocator.Locate(OperatingSystem.IsWindows() ? "mkvextract.exe" : "mkvextract", "tools", "mkvtoolnix");
            }
            catch (Exception installEx)
            {
                logger.LogWarning("MKVToolNix auto-download failed: {Reason}", installEx.Message);
                return null;
            }
        }

        var track = check.SubtitleTracks[0];
        var extension = track.Codec switch
        {
            var c when c.Contains("ASS", StringComparison.OrdinalIgnoreCase) => ".ass",
            var c when c.Contains("SSA", StringComparison.OrdinalIgnoreCase) => ".ssa",
            _ => ".srt"
        };
        var outputPath = Path.Combine(outputDirectory, $"media_subtitle_track_{track.TrackId}{extension}");

        try
        {
            var arguments = new List<string> { "tracks", mediaPath, $"{track.TrackId}:{outputPath}" };
            await processManager.ExecuteAsync(mkvextract, arguments, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning("mkvextract failed for '{Path}': {Reason}", mediaPath, ex.Message);
            return null;
        }

        if (!File.Exists(outputPath))
        {
            logger.LogWarning("mkvextract produced no output for track {TrackId} of '{Path}'.", track.TrackId, mediaPath);
            return null;
        }

        logger.LogInformation("Extracted subtitle track {TrackId} from '{Path}' -> '{Output}'.", track.TrackId, mediaPath, outputPath);
        return outputPath;
    }
}
