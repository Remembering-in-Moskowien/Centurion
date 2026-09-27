using System.ComponentModel;
using Spectre.Console.Cli;

namespace Centurion.Cli.Commands.Settings;

/// <summary>
/// Settings for the <c>build</c> command: render a Centurion intermediate file into
/// subtitle files (ASS / SRT / TXT). Format comes from --format or the -o extension, default ASS.
/// </summary>
public sealed class BuildSettings : GlobalCommandSettings
{
    /// <summary>Input Centurion intermediate file (.centurion.json).</summary>
    [CommandArgument(0, "<CENTURION_FILE>")]
    [Description("Centurion intermediate file (.centurion.json)")]
    public required FileInfo CenturionFile { get; init; }

    /// <summary>Output subtitle file; defaults to {input}.ass / .srt / .txt by format.</summary>
    [CommandOption("-o|--output <OUTPUT_FILE>")]
    [Description("Output subtitle file (default: {input}.ass / .srt / .txt)")]
    public FileInfo? OutputFile { get; init; }

    /// <summary>Output format: ass (default) / srt / txt; inferred from the -o extension when omitted.</summary>
    [CommandOption("-f|--format <FORMAT>")]
    [Description("Output format: ass (default), srt, txt")]
    public string? Format { get; init; }
}
