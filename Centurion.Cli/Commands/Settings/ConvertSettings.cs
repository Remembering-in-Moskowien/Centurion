using System.ComponentModel;
using Spectre.Console.Cli;

namespace Centurion.Cli.Commands.Settings;

/// <summary>
/// <c>convert</c> 命令的选项：将现有字幕文件转换为 ASS 格式。
/// </summary>
public sealed class ConvertSettings : CommandSettings
{
    /// <summary>
    /// 输入字幕文件（任意 SubtitlesParserV2 支持的格式）。
    /// </summary>
    [CommandArgument(0, "<INPUT_FILE>")]
    [Description("Input subtitle file (any format supported by SubtitlesParserV2)")]
    public required FileInfo InputFile { get; init; }

    /// <summary>
    /// 输出 ASS 文件路径；省略时在当前目录以输入文件名加 .ass 扩展名输出。
    /// </summary>
    [CommandOption("-o|--output <OUTPUT_FILE>")]
    [Description("Output ASS file path. If omitted, input filename with .ass extension in current directory.")]
    public FileInfo? OutputFile { get; init; }
}