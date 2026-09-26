using System.ComponentModel;
using Spectre.Console.Cli;

namespace Centurion.Cli.Commands.Settings;

/// <summary>
/// Options for the <c>update</c> command: check / download / apply a new version.
/// </summary>
public sealed class UpdateSettings : GlobalCommandSettings
{
    /// <summary>
    /// Whether to only check for a new version without downloading anything.
    /// </summary>
    [CommandOption("--check")]
    [Description("Only check for a new version, do not download anything")]
    public bool CheckOnly { get; init; }

    /// <summary>
    /// Whether to apply the update immediately: run the apply script after download (restarts this program).
    /// </summary>
    [CommandOption("--apply")]
    [Description("Apply the update immediately: download, then run the apply script (restarts the program)")]
    public bool Apply { get; init; }

    /// <summary>
    /// Manually specify the release asset name to download; auto-matched to the current platform when omitted.
    /// </summary>
    [CommandOption("--asset <NAME>")]
    [Description("Manually pick the release asset name to download (default: auto-match by platform)")]
    public string? AssetName { get; init; }
}
