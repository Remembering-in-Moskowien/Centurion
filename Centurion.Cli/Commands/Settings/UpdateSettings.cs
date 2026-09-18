using System.ComponentModel;
using Spectre.Console.Cli;

namespace Centurion.Cli.Commands.Settings;

public sealed class UpdateSettings : CommandSettings
{
    [CommandOption("--check")]
    [Description("Only check for a new version, do not download anything")]
    public bool CheckOnly { get; init; }

    [CommandOption("--apply")]
    [Description("Apply the update immediately: download, then run the apply script (restarts the program)")]
    public bool Apply { get; init; }

    [CommandOption("--asset <NAME>")]
    [Description("Manually pick the release asset name to download (default: auto-match by platform)")]
    public string? AssetName { get; init; }
}
