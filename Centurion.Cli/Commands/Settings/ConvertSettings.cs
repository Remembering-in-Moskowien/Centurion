using System.ComponentModel;
using Spectre.Console.Cli;

namespace Centurion.Cli.Commands.Settings;

/// <summary>
/// <c>convert</c> 命令的选项：将现有字幕文件转换为 Centurion 中间文件（*.centurion.json）。
/// 中间文件包含句子与词级时间轴（ASS karaoke 标签解析）的完整详细信息，供后续命令继续处理。
/// </summary>
public sealed class ConvertSettings : GlobalCommandSettings
{
    /// <summary>
    /// 输入字幕文件（任意 SubtitlesParserV2 支持的格式）。
    /// </summary>
    [CommandArgument(0, "<INPUT_FILE>")]
    [Description("Input subtitle file (any format supported by SubtitlesParserV2)")]
    public required FileInfo InputFile { get; init; }

    /// <summary>
    /// 输出中间文件路径；省略时以输入文件名加 .centurion.json 扩展名输出。
    /// </summary>
    [CommandOption("-o|--output <OUTPUT_FILE>")]
    [Description("Output Centurion intermediate file (.centurion.json). If omitted, input filename with .centurion.json.")]
    public FileInfo? OutputFile { get; init; }
}