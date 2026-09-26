using System.ComponentModel;
using Spectre.Console.Cli;

namespace Centurion.Cli.Commands.Settings;

/// <summary>
/// Options for the <c>quality</c> command: quality report (.quality.json + .quality.html),
/// auto-fix (--fix) and CI thresholds (--fail-on).
/// </summary>
public sealed class QualitySettings : GlobalCommandSettings
{
    /// <summary>
    /// Centurion intermediate file to evaluate (.centurion.json).
    /// </summary>
    [CommandArgument(0, "<INPUT_FILE>")]
    [Description("Centurion intermediate file (.centurion.json)")]
    public required FileInfo InputFile { get; init; }

    /// <summary>
    /// Output IR path (fix results written with --fix; default &lt;input&gt;.quality.centurion.json).
    /// </summary>
    [CommandOption("-o|--output <OUTPUT_FILE>")]
    [Description("Output Centurion intermediate file (default: {input}.quality.centurion.json)")]
    public FileInfo? OutputFile { get; init; }

    /// <summary>
    /// Auto-fixes common issues (overlaps/too-short/line width/CPS) and writes fixed sentences back to the output IR.
    /// </summary>
    [CommandOption("-f|--fix")]
    [Description("Auto-fix common issues (overlap/short/line-length/CPS) and write fixed intermediate file")]
    public bool Fix { get; init; }

    /// <summary>
    /// Custom HTML report path (defaults to the same name/directory as .quality.json).
    /// </summary>
    [CommandOption("--html <HTML_FILE>")]
    [Description("Custom HTML report path (default: {input}.quality.html)")]
    public FileInfo? HtmlFile { get; init; }

    /// <summary>
    /// Repeatable CI threshold rules: --fail-on cps&gt;20 --fail-on coverage&lt;95.
    /// Metrics: cps/maxcps/meancps (speed), linelen (over-width lines), overlap (overlapping lines),
    /// minms/maxms (shortest/longest duration), coverage (mapped coverage %), confidence (mean confidence 0~1),
    /// glossary (glossary hit rate %), lengthdev (length deviation), ttsdev (mean TTS alignment error ms).
    /// Any unmet rule exits with code 1 (CI failure).
    /// </summary>
    [CommandOption("--fail-on <RULE>")]
    [Description("CI threshold rule, repeatable: --fail-on cps>20 --fail-on coverage<95")]
    public string[] FailOn { get; init; } = [];
}
