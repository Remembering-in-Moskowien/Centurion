using System.ComponentModel;
using Spectre.Console.Cli;

namespace Centurion.Cli.Commands.Settings;
public sealed class ConvertSettings : CommandSettings
{
    [CommandArgument(0, "<INPUT_FILE>")]
    [Description("Input subtitle file (any format supported by SubtitlesParserV2)")]
    public required FileInfo InputFile { get; init; }

    [CommandOption("-o|--output <OUTPUT_FILE>")]
    [Description("Output ASS file path. If omitted, input filename with .ass extension in current directory.")]
    public FileInfo? OutputFile { get; init; }
}