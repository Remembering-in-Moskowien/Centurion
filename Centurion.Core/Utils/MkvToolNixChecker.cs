using System.Text.RegularExpressions;
using Centurion.Abstractions;
using Centurion.Abstractions.Exceptions;
using Centurion.Core.Infrastructure;
using Centurion.Core.Managers;
using Centurion.Models.Workflow;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Utils;

/// <summary>
/// 基于 mkvtoolnix（mkvmerge -i）的媒体字幕轨检查器：
/// 探测输入媒体中的既有字幕轨，供生成/校准/打轴命令在开工前预警。
/// mkvmerge 缺失或输入非容器文件时按警告跳过（非致命）。
/// </summary>
public sealed class MkvToolNixChecker(
    IBinaryLocator binaryLocator,
    ProcessManager processManager,
    MkvtoolnixManager mkvtoolnixManager,
    ILogger<MkvToolNixChecker> logger)
{
    private static readonly Regex TrackLineRegex = new(
        @"^(?:Track ID|轨道 ID) (\d+): (\w+) \(([^)]*)\)(?: \[([^\]]+)\])?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// 检查指定媒体文件中的字幕轨。
    /// </summary>
    /// <param name="mediaPath">媒体文件路径（mkv/mp4/ts 等容器）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>检查结果；mkvmerge 不可用或输入不可解析时为 Checked=false 的结果（含 Message）。</returns>
    public async Task<SubtitleTrackCheckResult> CheckAsync(string mediaPath, CancellationToken cancellationToken)
    {
        var skip = (string message) => new SubtitleTrackCheckResult
        {
            SourceFile = mediaPath,
            Checked = false,
            Message = message
        };

        string mkvmerge;
        try
        {
            mkvmerge = binaryLocator.Locate(OperatingSystem.IsWindows() ? "mkvmerge.exe" : "mkvmerge", "tools/mkvtoolnix");
        }
        catch (BinaryNotFoundException ex)
        {
            logger.LogWarning("mkvmerge not found ({Path}); attempting automatic MKVToolNix download...", ex.BinaryName);
            try
            {
                await mkvtoolnixManager.EnsureInstalledAsync(cancellationToken);
                mkvmerge = binaryLocator.Locate(OperatingSystem.IsWindows() ? "mkvmerge.exe" : "mkvmerge", "tools", "mkvtoolnix");
            }
            catch (Exception installEx)
            {
                logger.LogWarning("MKVToolNix auto-download failed: {Reason}", installEx.Message);
                return skip(installEx.Message);
            }
        }

        string output;
        try
        {
            output = await processManager.ExecuteAsync(mkvmerge, ["-i", mediaPath], cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning("mkvmerge failed for '{Path}'; subtitle track check skipped: {Reason}", mediaPath, ex.Message);
            return skip(ex.Message);
        }

        var allTracks = ParseTrackLines(output);

        if (allTracks.Count == 0)
        {
            logger.LogWarning("mkvmerge produced no track info for '{Path}'; check skipped.", mediaPath);
            return skip("No track information returned by mkvmerge.");
        }

        var subtitleTracks = allTracks.Where(t => t.IsSubtitle).ToList();
        return new SubtitleTrackCheckResult
        {
            SourceFile = mediaPath,
            Checked = true,
            HasSubtitleTracks = subtitleTracks.Count > 0,
            AllTracks = allTracks,
            SubtitleTracks = subtitleTracks
        };
    }

    /// <summary>
    /// 解析 mkvmerge -i 输出的轨道行（"Track ID 0: video (V_MPEG4/ISO/AVC) [language:eng, name:English]"）。
    /// </summary>
    /// <param name="output">mkvmerge -i 的完整标准输出。</param>
    /// <returns>解析出的轨道列表。</returns>
    internal static List<MkvTrackInfo> ParseTrackLines(string output)
    {
        var allTracks = new List<MkvTrackInfo>();
        foreach (var line in output.Split('\n'))
        {
            var match = TrackLineRegex.Match(line.Trim());
            if (!match.Success)
                continue;

            var attributes = match.Groups[4].Success
                ? ParseAttributes(match.Groups[4].Value)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            allTracks.Add(new MkvTrackInfo
            {
                TrackId = int.Parse(match.Groups[1].Value),
                Type = match.Groups[2].Value.ToLowerInvariant(),
                Codec = match.Groups[3].Value,
                Language = attributes.TryGetValue("language", out var lang) ? lang : null,
                Name = attributes.TryGetValue("name", out var name) ? name : null
            });
        }
        return allTracks;
    }

    private static Dictionary<string, string> ParseAttributes(string raw)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colon = pair.IndexOf(':');
            if (colon <= 0)
                continue;
            var key = pair[..colon].Trim();
            var value = pair[(colon + 1)..].Trim();
            // mkvmerge 输出语言随系统本地化（gettext），语言/名称属性键存在英文与中文两种形态
            if (key.Equals("language", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("语言", StringComparison.OrdinalIgnoreCase))
            {
                dict["language"] = value;
            }
            else if (key.Equals("name", StringComparison.OrdinalIgnoreCase) ||
                     key.Equals("名称", StringComparison.OrdinalIgnoreCase))
            {
                dict["name"] = value;
            }
        }
        return dict;
    }
}
