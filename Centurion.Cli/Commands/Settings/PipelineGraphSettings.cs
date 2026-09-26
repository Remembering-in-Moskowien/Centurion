using System.ComponentModel;
using Spectre.Console.Cli;

namespace Centurion.Cli.Commands.Settings;

/// <summary>
/// <c>pipeline graph</c> 命令的选项：渲染指定命令的 DAG 管线拓扑（节点、依赖、条件、重试/降级标注）。
/// </summary>
public sealed class PipelineGraphSettings : GlobalCommandSettings
{
    /// <summary>
    /// 要渲染的命令 DAG：asr（默认）或 translate。
    /// </summary>
    [CommandOption("-c|--command <COMMAND>")]
    [Description("Pipeline command to render: asr (default) or translate")]
    public string Command { get; init; } = "asr";

    /// <summary>
    /// 输出文件路径（.mmd / .txt / .html）；省略时输出到控制台。
    /// </summary>
    [CommandOption("-o|--output <OUTPUT_FILE>")]
    [Description("Write graph to file (.mmd/.txt/.html); defaults to console")]
    public FileInfo? OutputFile { get; init; }
}
