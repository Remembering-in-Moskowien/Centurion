using System.ComponentModel;
using Spectre.Console.Cli;

namespace Centurion.Cli.Commands.Settings;

/// <summary>
/// Options for the <c>init</c> wizard: all optional; missing values are asked
/// interactively, or defaulted when <c>--yes</c> is passed.
/// </summary>
public sealed class InitSettings : GlobalCommandSettings
{
    /// <summary>Media file (video/audio); prompted when omitted.</summary>
    [CommandOption("--media <FILE>")]
    [Description("Media file (video/audio) to process")]
    public FileInfo? Media { get; init; }

    /// <summary>Workflow: asr / ocr / from-script / translate / dub / correct.</summary>
    [CommandOption("-w|--workflow <NAME>")]
    [Description("Workflow to scaffold: asr / ocr / from-script / translate / dub / correct")]
    public string? Workflow { get; init; }

    /// <summary>Output subtitle format: ass / srt / txt (default ass).</summary>
    [CommandOption("-f|--format <FORMAT>")]
    [Description("Output subtitle format: ass / srt / txt (default ass)")]
    public string? Format { get; init; }

    /// <summary>Provider profile: offline / fast / quality / cheap.</summary>
    [CommandOption("-p|--profile <NAME>")]
    [Description("Provider profile: offline / fast / quality / cheap")]
    public string? Profile { get; init; }

    /// <summary>Translation target language code (e.g. zh, en).</summary>
    [CommandOption("--target <CODE>")]
    [Description("Translation target language (e.g. zh, en)")]
    public string? Target { get; init; }

    /// <summary>Config output directory (default: current directory).</summary>
    [CommandOption("-o|--output <DIR>")]
    [Description("Config output directory (default: current directory)")]
    public DirectoryInfo? Output { get; init; }

    /// <summary>Non-interactive: generate the config with defaults (no prompts).</summary>
    [CommandOption("-y|--yes")]
    [Description("Non-interactive: use defaults without prompting")]
    public bool Yes { get; init; }
}
