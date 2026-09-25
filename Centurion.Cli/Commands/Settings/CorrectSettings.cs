using Centurion.Models.Workflow;
using System.ComponentModel;
using Centurion.Abstractions;
using Spectre.Console.Cli;

namespace Centurion.Cli.Commands.Settings;

/// <summary>
/// <c>correct</c> 命令的选项：对照源音频和/或参考脚本校正已有字幕（输入为 Centurion 中间文件）。
/// </summary>
public sealed class CorrectSettings : CommandSettings
{
    /// <summary>
    /// 待校正的 Centurion 中间文件（.centurion.json）。
    /// </summary>
    [CommandArgument(0, "<CENTURION_FILE>")]
    [Description("Centurion intermediate file (.centurion.json)")]
    public required FileInfo CenturionFile { get; init; }

    /// <summary>
    /// 输出中间文件路径；省略时以输入名加 .corrected.centurion.json 输出。
    /// </summary>
    [CommandOption("-o|--output <OUTPUT_FILE>")]
    [Description("Output Centurion intermediate file (default: <input>.corrected.centurion.json)")]
    public FileInfo? OutputFile { get; init; }

    /// <summary>
    /// 用于时间轴校正的音频/媒体文件（时间轴类策略时必填；输入为媒体文件时可省略）。
    /// </summary>
    [CommandOption("--audio <AUDIO_FILE>")]
    [Description("Audio/media file used for timeline correction (required for timeline strategies)")]
    public FileInfo? AudioFile { get; init; }

    /// <summary>
    /// 用于文本校正的参考脚本文本文件（文本类策略时必填）。
    /// </summary>
    [CommandOption("--script <SCRIPT_FILE>")]
    [Description("Script file used for text correction")]
    public FileInfo? ScriptFile { get; init; }

    /// <summary>
    /// 校正策略：timeline-only（仅时间轴）、text-only（仅文本）、both（两者）。
    /// </summary>
    [CommandOption("-s|--strategy <MODE>")]
    [Description("Correction strategy: timeline-only, text-only, both")]
    public string Strategy { get; init; } = "both";

    /// <summary>
    /// 报告时使用的最大允许漂移量（毫秒）。
    /// </summary>
    [CommandOption("--max-drift <MS>")]
    [Description("Maximum drift used for reporting")]
    public int MaxDrift { get; init; } = 1500;

    /// <summary>
    /// 文本模糊匹配的最低相似度阈值（0–1）。
    /// </summary>
    [CommandOption("--fuzzy-threshold <T>")]
    [Description("Minimum text similarity from 0 to 1")]
    public double FuzzyThreshold { get; init; } = 0.72;

    /// <summary>
    /// 是否禁用音频重采样。
    /// </summary>
    [CommandOption("--disable-audio-resampling")]
    [Description("Disable audio resampling")]
    public bool DisableAudioResampling { get; init; }

    /// <summary>
    /// 是否禁用高通滤波器。
    /// </summary>
    [CommandOption("--disable-audio-highpass")]
    [Description("Disable the high-pass filter")]
    public bool DisableAudioHighPass { get; init; }

    /// <summary>
    /// 是否禁用响度归一化。
    /// </summary>
    [CommandOption("--disable-audio-loudness")]
    [Description("Disable loudness normalization")]
    public bool DisableAudioLoudness { get; init; }

    // ----- 人声分离模块 (Vocal Separation) -----
    /// <summary>
    /// 是否在对齐前用 Demucs 分离人声（仅适用于音乐/背景音乐较重的素材）。
    /// </summary>
    [CommandOption("--vs|--vocal-separation")]
    [Description("Separate vocals with Demucs before alignment (only for music/BGM-heavy media)")]
    public bool VocalSeparation { get; init; } = false;

    /// <summary>
    /// Demucs 人声分离模型名称，默认 htdemucs。
    /// </summary>
    [CommandOption("--vocal-separation-model <MODEL>")]
    [Description("Demucs model name, default htdemucs")]
    public string VocalSeparationModel { get; init; } = "htdemucs";

    // ----- 推理设备 (Device) -----
    /// <summary>
    /// 推理设备偏好：auto、cpu、cuda、vulkan、directml（GPU 工具会自动下载对应变体）。
    /// </summary>
    [CommandOption("--device <DEVICE>")]
    [Description("Inference device: auto, cpu, cuda, vulkan, directml (GPU tool builds auto-downloaded)")]
    public InferenceDevice Device { get; init; } = InferenceDevice.Auto;

    /// <summary>
    /// 音频语言代码（如 en、zh），默认 en；中文音频请传 zh 以获得更稳的转录与分句。
    /// </summary>
    [CommandOption("-l|--language <LANG>")]
    [Description("Audio language code, default en")]
    public string Language { get; init; } = "en";

    /// <summary>
    /// 是否生成 ASS 卡拉OK逐字效果。
    /// </summary>
    [CommandOption("-k|--karaoke")]
    [Description("Generate ASS karaoke effects")]
    public bool Karaoke { get; init; }

    /// <summary>
    /// 是否在字幕文本前显示说话人标签；默认开启，传 --no-speaker-labels 关闭。
    /// 说话人信息始终写入 ASS 的 Name 字段，不受本开关影响。
    /// </summary>
    [CommandOption("--no-speaker-labels")]
    [Description("Do not prefix subtitle text with speaker labels (Name field is always written)")]
    public bool ShowSpeakerLabels { get; init; } = true;

    /// <summary>
    /// 是否启用 Hunspell 拼写检查（检测字幕中的可疑词并输出报告）。
    /// </summary>
    [CommandOption("--spellcheck")]
    [Description("Check subtitle text with Hunspell and report suspicious words")]
    public bool SpellCheck { get; init; }

    /// <summary>
    /// Hunspell 词典前缀，默认 en_US；缺失时自动下载（仅支持默认前缀）。
    /// </summary>
    [CommandOption("--hunspell-dict <PREFIX>")]
    [Description("Hunspell dictionary prefix, default en_US (auto-downloaded if missing)")]
    public string HunspellDictionary { get; init; } = "en_US";

}