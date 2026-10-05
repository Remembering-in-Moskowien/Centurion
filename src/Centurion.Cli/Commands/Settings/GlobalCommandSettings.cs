using System.ComponentModel;
using Spectre.Console.Cli;

namespace Centurion.Cli.Commands.Settings;

/// <summary>
/// Base class with global options shared by every command:
/// <list type="bullet">
/// <item><c>--json</c>: machine-readable JSON summary only (suppresses human lines; script-consumable);</item>
/// <item><c>--agent</c>: LLM-friendly plain-text output (fixed INFO/OK/WARN/ERROR prefixes, no ANSI/timestamps/spinners/prompts);</item>
/// <item><c>--dry-run</c>: preview the DAG, models and estimated cost without executing.</item>
/// </list>
/// All options are optional and keep old usage backward compatible.
/// </summary>
public abstract class GlobalCommandSettings : CommandSettings
{
    /// <summary>Output the result summary as machine-readable JSON (script-consumable; suppresses console lines).</summary>
    [CommandOption("--json")]
    [Description("Output the result summary as machine-readable JSON (suppresses human lines)")]
    public bool Json { get; init; }

    /// <summary>Output plain, LLM-friendly text: fixed INFO/OK/WARN/ERROR prefixes, no ANSI colors, timestamps, spinners or interactive prompts.</summary>
    [CommandOption("--agent")]
    [Description("LLM-friendly plain-text output (no ANSI/timestamps/spinners/prompts; INFO/OK/WARN/ERROR prefixed)")]
    public bool Agent { get; init; }

    /// <summary>Preview the DAG topology, involved models and estimated cost without running any operator.</summary>
    [CommandOption("--dry-run")]
    [Description("Preview DAG, models and estimated cost without executing")]
    public bool DryRun { get; init; }
}
