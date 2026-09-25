using System.ComponentModel;
using Spectre.Console.Cli;

namespace Centurion.Cli.Commands.Settings;

/// <summary>
/// <c>build</c> 命令设置：把 Centurion 中间文件渲染为字幕文件（ASS / SRT / TXT）。
/// 输出格式由 --format 指定，或从 -o 输出文件扩展名推断，默认 ASS。
/// </summary>
public sealed class BuildSettings : CommandSettings
{
    /// <summary>输入的 Centurion 中间文件（.centurion.json）。</summary>
    [CommandArgument(0, "<CENTURION_FILE>")]
    [Description("Centurion intermediate file (.centurion.json)")]
    public required FileInfo CenturionFile { get; init; }

    /// <summary>输出字幕文件路径；省略时按格式以输入同名 .ass / .srt / .txt 输出。</summary>
    [CommandOption("-o|--output <OUTPUT_FILE>")]
    [Description("Output subtitle file (default: <input>.ass / .srt / .txt)")]
    public FileInfo? OutputFile { get; init; }

    /// <summary>输出格式：ass（默认）/ srt / txt；省略时从 -o 扩展名推断。</summary>
    [CommandOption("-f|--format <FORMAT>")]
    [Description("Output format: ass (default), srt, txt")]
    public string? Format { get; init; }
}
