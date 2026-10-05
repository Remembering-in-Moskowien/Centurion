using System.Text.RegularExpressions;
using Centurion.Abstractions;
using Centurion.Abstractions.Exceptions;
using Centurion.Core.Capabilities.Infrastructure;using Centurion.Models.Workflow;
using Microsoft.Extensions.Logging;
using Centurion.Core.Capabilities.Managers.Media;
using Centurion.Core.Capabilities.Managers.Runtime;
namespace Centurion.Core.Utils.Media;

/// <summary>
/// Subtitle track checker based on mkvtoolnix (mkvmerge -i):
/// detects existing subtitle tracks in the input media so that the generate/align/timing
/// commands can warn up front. Skips with a warning (non-fatal) when mkvmerge is missing
/// or the input is not a container file.
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
    /// Check the subtitle tracks in the given media file.
    /// </summary>
    /// <param name="mediaPath">Path to the media file (mkv/mp4/ts, etc. containers).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The check result; when mkvmerge is unavailable or the input cannot be parsed, a result with Checked=false (including Message).</returns>
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
    /// Parse a track line from mkvmerge -i output ("Track ID 0: video (V_MPEG4/ISO/AVC) [language:eng, name:English]").
    /// </summary>
    /// <param name="output">The full standard output of mkvmerge -i.</param>
    /// <returns>The parsed track list.</returns>
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
            // mkvmerge output language is localized by the system (gettext); the language/name attribute keys exist in both English and Chinese forms
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
