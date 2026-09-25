using System.ComponentModel;
using Centurion.Models.Workflow;
using Spectre.Console.Cli;

namespace Centurion.Cli.Commands.Settings;

/// <summary>
/// 独立算子小命令的通用选项：输入（媒体或中间文件，取决于命令）、输出、语言与设备。
/// 供 transcribe / vocalsep / diarize / split / clean / align / spellcheck / quality 共用。
/// </summary>
public sealed class OperatorSettings : CommandSettings
{
    /// <summary>输入文件：Centurion 中间文件（.centurion.json），部分命令也接受媒体文件。</summary>
    [CommandArgument(0, "<INPUT_FILE>")]
    [Description("Input: Centurion intermediate file (.centurion.json), or a media file for source commands")]
    public required FileInfo InputFile { get; init; }

    /// <summary>输出中间文件路径；省略时以输入名加 .&lt;命令名&gt;.centurion.json 输出。</summary>
    [CommandOption("-o|--output <OUTPUT_FILE>")]
    [Description("Output intermediate file (default: <input>.<command>.centurion.json)")]
    public FileInfo? OutputFile { get; init; }

    /// <summary>音频语言（媒体输入的源头命令用），默认 en。</summary>
    [CommandOption("-l|--language <LANG>")]
    [Description("Audio language for media-input commands (default: en)")]
    public string Language { get; init; } = "en";

    /// <summary>推理设备：auto / cpu / cuda / vulkan / directml。</summary>
    [CommandOption("--device <DEVICE>")]
    [Description("Inference device: auto (default), cpu, cuda, vulkan, directml")]
    public InferenceDevice Device { get; init; } = InferenceDevice.Auto;
}
