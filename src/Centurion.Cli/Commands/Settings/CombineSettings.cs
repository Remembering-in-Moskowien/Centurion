using System.ComponentModel;
using Spectre.Console.Cli;

namespace Centurion.Cli.Commands.Settings;

/// <summary>
/// Options for the <c>combine</c> command: merge multiple subtitle sources (media tracks or
/// subtitle files) into a single timeline and save either the intermediate IR or a rendered
/// subtitle file (.ass/.srt/.txt).
/// </summary>
public sealed class CombineSettings : GlobalCommandSettings
{
    /// <summary>
    /// Media files or subtitle files to merge. For media files, all extractable subtitle tracks are
    /// enumerated and merged; a track filter can be supplied via --track.
    /// </summary>
    [CommandArgument(0, "[INPUTS]")]
    [Description("Media files and/or subtitle files to merge")]
    public string[] Inputs { get; init; } = [];

    /// <summary>Optional subtitle file(s) to merge in addition to media inputs.</summary>
    [CommandOption("--subtitle <FILE>")]
    [Description("Subtitle file to merge in addition to media inputs; repeatable")]
    public string[] SubtitleFiles { get; init; } = [];

    /// <summary>Optional track IDs to include when reading a media container.</summary>
    [CommandOption("--track <TRACK_ID>")]
    [Description("Subtitle track ID to include; repeatable (omit to merge all subtitle tracks)")]
    public int[] Tracks { get; init; } = [];

    /// <summary>
    /// Output path. Defaults to a .centurion.json intermediate document; if a final subtitle
    /// extension (.ass/.srt/.txt) is used, the merged result is rendered directly.
    /// </summary>
    [CommandOption("-o|--output <OUTPUT_FILE>")]
    [Description("Output file (.centurion.json, .ass, .srt or .txt)")]
    public FileInfo? OutputFile { get; init; }

    /// <summary>Preferred language for subtitle parsing and display (default: en).</summary>
    [CommandOption("-l|--language <LANG>")]
    [Description("Language code used while parsing subtitle text and media track metadata")]
    public string Language { get; init; } = "en";

    /// <summary>Output format when emitting final subtitles directly instead of an IR file.</summary>
    [CommandOption("-f|--format <FORMAT>")]
    [Description("Output format for direct subtitle rendering: ass, srt, txt")]
    public string? Format { get; init; }

    /// <summary>Time tolerance used by duplicate removal in milliseconds.</summary>
    [CommandOption("--dedupe-tolerance-ms <MS>")]
    [Description("Duplicate-removal time tolerance in milliseconds (default: 500)")]
    public double DedupeToleranceMs { get; init; } = 500.0;

    /// <summary>Text similarity floor used by duplicate removal; values closer to 1.0 are stricter.</summary>
    [CommandOption("--dedupe-similarity <VALUE>")]
    [Description("Duplicate-removal similarity threshold between 0 and 1 (default: 0.7)")]
    public double DedupeSimilarity { get; init; } = 0.7;
}
