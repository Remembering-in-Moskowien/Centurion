using System.ComponentModel;
using Spectre.Console.Cli;

namespace Centurion.Cli.Commands.Settings;

public sealed class FromScriptSettings : CommandSettings
{
    [CommandArgument(0, "<INPUT_FILE>")]
    [Description("Input media file")]
    public required FileInfo InputFile { get; init; }

    [CommandArgument(1, "<SCRIPT_FILE>")]
    [Description("Plain-text script file")]
    public required FileInfo ScriptFile { get; init; }

    [CommandOption("-o|--output <OUTPUT_FILE>")]
    [Description("Output ASS subtitle file")]
    public FileInfo? OutputFile { get; init; }

    [CommandOption("-l|--language <LANG>")]
    [Description("Audio language code, default en")]
    public string Language { get; init; } = "en";

    [CommandOption("-t|--transcriber <ENGINE>")]
    [Description("Transcription engine")]
    public string Transcriber { get; init; } = "whisper";

    [CommandOption("--tm|--transcriber-model <MODEL>")]
    [Description("Transcription model")]
    public string? TranscriberModel { get; init; } = "base";

    [CommandOption("--enable-alignment")]
    [Description("Enable forced alignment")]
    public bool EnableAlignment { get; init; } = true;

    [CommandOption("--max-cps <CPS>")]
    [Description("Maximum displayed characters per second")]
    public double MaxCps { get; init; } = 5.0;

    [CommandOption("--max-chars-per-line <CHARS>")]
    [Description("Maximum characters per subtitle line")]
    public int MaxCharsPerLine { get; init; } = 18;

    [CommandOption("--coverage-threshold <RATIO>")]
    [Description("Warning threshold for script-to-audio coverage")]
    public double CoverageThreshold { get; init; } = 0.92;

    [CommandOption("--fill-gap")]
    [Description("Render missing script words as an ellipsis")]
    public bool FillGapWithEllipsis { get; init; } = true;
}