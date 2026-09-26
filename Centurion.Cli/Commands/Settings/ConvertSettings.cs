using System.ComponentModel;
using Spectre.Console.Cli;

namespace Centurion.Cli.Commands.Settings;

/// <summary>
/// Options for the <c>convert</c> command: convert existing subtitle files into
/// Centurion intermediate files (*.centurion.json). The IR carries full sentence and
/// word-level timeline detail (ASS karaoke tags parsed) for downstream commands.
/// </summary>
public sealed class ConvertSettings : GlobalCommandSettings
{
    /// <summary>
    /// Input subtitle file (any format supported by SubtitlesParserV2).
    /// </summary>
    [CommandArgument(0, "<INPUT_FILE>")]
    [Description("Input subtitle file (any format supported by SubtitlesParserV2)")]
    public required FileInfo InputFile { get; init; }

    /// <summary>
    /// Output IR file path; defaults to the input name plus .centurion.json.
    /// </summary>
    [CommandOption("-o|--output <OUTPUT_FILE>")]
    [Description("Output Centurion intermediate file (.centurion.json). If omitted, input filename with .centurion.json.")]
    public FileInfo? OutputFile { get; init; }
}