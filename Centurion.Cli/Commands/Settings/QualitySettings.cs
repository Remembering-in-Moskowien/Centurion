using System.ComponentModel;
using Spectre.Console.Cli;

namespace Centurion.Cli.Commands.Settings;

/// <summary>
/// <c>quality</c> 命令的选项：质量报告（.quality.json + .quality.html）、
/// 自动修复（--fix）与 CI 阈值（--fail-on）。
/// </summary>
public sealed class QualitySettings : GlobalCommandSettings
{
    /// <summary>
    /// 待评估的 Centurion 中间文件（.centurion.json）。
    /// </summary>
    [CommandArgument(0, "<INPUT_FILE>")]
    [Description("Centurion intermediate file (.centurion.json)")]
    public required FileInfo InputFile { get; init; }

    /// <summary>
    /// 输出中间文件路径（--fix 时写修复结果；默认 &lt;input&gt;.quality.centurion.json）。
    /// </summary>
    [CommandOption("-o|--output <OUTPUT_FILE>")]
    [Description("Output Centurion intermediate file (default: <input>.quality.centurion.json)")]
    public FileInfo? OutputFile { get; init; }

    /// <summary>
    /// 自动修复常见问题（重叠/过短/行宽/CPS），并把修复后的句子写回输出中间文件。
    /// </summary>
    [CommandOption("-f|--fix")]
    [Description("Auto-fix common issues (overlap/short/line-length/CPS) and write fixed intermediate file")]
    public bool Fix { get; init; }

    /// <summary>
    /// 自定义 HTML 报告输出路径（默认与 .quality.json 同名同目录）。
    /// </summary>
    [CommandOption("--html <HTML_FILE>")]
    [Description("Custom HTML report path (default: <input>.quality.html)")]
    public FileInfo? HtmlFile { get; init; }

    /// <summary>
    /// CI 阈值规则，可重复：--fail-on cps&gt;20 --fail-on coverage&lt;95。
    /// 指标：cps/maxcps/meancps（语速），linelen（超行宽行数），overlap（重叠行数），
    /// minms/maxms（最短/最长时长），coverage（映射覆盖率 %），confidence（平均置信度 0~1），
    /// glossary（术语命中率 %），lengthdev（长度偏差），ttsdev（TTS 平均对齐误差 ms）。
    /// 任一不满足即退出码 1（CI 失败）。
    /// </summary>
    [CommandOption("--fail-on <RULE>")]
    [Description("CI threshold rule, repeatable: --fail-on cps>20 --fail-on coverage<95")]
    public string[] FailOn { get; init; } = [];
}
