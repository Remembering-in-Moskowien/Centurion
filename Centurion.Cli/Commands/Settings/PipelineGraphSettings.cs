using System.ComponentModel;
using Spectre.Console.Cli;

namespace Centurion.Cli.Commands.Settings;

/// <summary>
/// Options for the <c>pipeline graph</c> command: render a command's DAG topology (nodes, dependencies, conditions, retry/degrade labels).
/// </summary>
public sealed class PipelineGraphSettings : GlobalCommandSettings
{
    /// <summary>
    /// The command DAG to render: asr (default) or translate.
    /// </summary>
    [CommandOption("-c|--command <COMMAND>")]
    [Description("Pipeline command to render: asr (default) or translate")]
    public string Command { get; init; } = "asr";

    /// <summary>
    /// Output file path (.mmd / .txt / .html); prints to the console when omitted.
    /// </summary>
    [CommandOption("-o|--output <OUTPUT_FILE>")]
    [Description("Write graph to file (.mmd/.txt/.html); defaults to console")]
    public FileInfo? OutputFile { get; init; }
}
