using System.ComponentModel;
using Spectre.Console.Cli;

namespace Centurion.Cli.Commands.Settings;

/// <summary>
/// <c>init</c> 向导选项：全部可选；未提供时交互式提问，<c>--yes</c> 时用默认值非交互生成。
/// </summary>
public sealed class InitSettings : GlobalCommandSettings
{
    /// <summary>媒体文件（视频/音频）；缺省交互提问。</summary>
    [CommandOption("--media <FILE>")]
    [Description("Media file (video/audio) to process")]
    public FileInfo? Media { get; init; }

    /// <summary>工作流：asr / ocr / from-script / translate / dub / correct。</summary>
    [CommandOption("-w|--workflow <NAME>")]
    [Description("Workflow to scaffold: asr / ocr / from-script / translate / dub / correct")]
    public string? Workflow { get; init; }

    /// <summary>目标字幕格式：ass / srt / txt（默认 ass）。</summary>
    [CommandOption("-f|--format <FORMAT>")]
    [Description("Output subtitle format: ass / srt / txt (default ass)")]
    public string? Format { get; init; }

    /// <summary>Provider profile：offline / fast / quality / cheap。</summary>
    [CommandOption("-p|--profile <NAME>")]
    [Description("Provider profile: offline / fast / quality / cheap")]
    public string? Profile { get; init; }

    /// <summary>翻译目标语言代码（如 zh、en）。</summary>
    [CommandOption("--target <CODE>")]
    [Description("Translation target language (e.g. zh, en)")]
    public string? Target { get; init; }

    /// <summary>输出目录（默认当前目录）。</summary>
    [CommandOption("-o|--output <DIR>")]
    [Description("Config output directory (default: current directory)")]
    public DirectoryInfo? Output { get; init; }

    /// <summary>非交互：全部使用默认值生成配置（不提问）。</summary>
    [CommandOption("-y|--yes")]
    [Description("Non-interactive: use defaults without prompting")]
    public bool Yes { get; init; }
}
