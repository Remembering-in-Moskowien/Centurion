using System.ComponentModel;
using Spectre.Console.Cli;

namespace Centurion.Cli.Commands.Settings;

/// <summary>
/// <c>update</c> 命令的选项：检查 / 下载 / 应用新版本。
/// </summary>
public sealed class UpdateSettings : GlobalCommandSettings
{
    /// <summary>
    /// 是否仅检查新版本而不下载任何内容。
    /// </summary>
    [CommandOption("--check")]
    [Description("Only check for a new version, do not download anything")]
    public bool CheckOnly { get; init; }

    /// <summary>
    /// 是否立即应用更新：下载后运行应用脚本（会重启本程序）。
    /// </summary>
    [CommandOption("--apply")]
    [Description("Apply the update immediately: download, then run the apply script (restarts the program)")]
    public bool Apply { get; init; }

    /// <summary>
    /// 手动指定要下载的发布资产名称；省略时按当前平台自动匹配。
    /// </summary>
    [CommandOption("--asset <NAME>")]
    [Description("Manually pick the release asset name to download (default: auto-match by platform)")]
    public string? AssetName { get; init; }
}
