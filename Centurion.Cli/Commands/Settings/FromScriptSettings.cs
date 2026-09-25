using Centurion.Models.Workflow;
using System.ComponentModel;
using Centurion.Abstractions;
using Spectre.Console.Cli;

namespace Centurion.Cli.Commands.Settings;

/// <summary>
/// <c>from-script</c> 命令的选项：将纯文本脚本对齐到音视频并生成带时间轴的字幕。
/// </summary>
public sealed class FromScriptSettings : CommandSettings
{

    /// <summary>
    /// 输入音视频媒体文件。
    /// </summary>
    [CommandArgument(0, "<INPUT_FILE>")]
    [Description("Input media file")]
    public required FileInfo InputFile { get; init; }

    /// <summary>
    /// 纯文本脚本文件。
    /// </summary>
    [CommandArgument(1, "<SCRIPT_FILE>")]
    [Description("Plain-text script file")]
    public required FileInfo ScriptFile { get; init; }

    /// <summary>
    /// 输出 ASS 字幕文件路径；省略时以输入文件名加 .ass 扩展名输出。
    /// </summary>
    [CommandOption("-o|--output <OUTPUT_FILE>")]
    [Description("Output ASS subtitle file")]
    public FileInfo? OutputFile { get; init; }

    /// <summary>
    /// 音频语言代码，默认 en。
    /// </summary>
    [CommandOption("-l|--language <LANG>")]
    [Description("Audio language code, default en")]
    public string Language { get; init; } = "en";

    /// <summary>
    /// 转录引擎名称。
    /// </summary>
    [CommandOption("-t|--transcriber <ENGINE>")]
    [Description("Transcription engine")]
    public string Transcriber { get; init; } = "whisper";

    /// <summary>
    /// 转录模型名称。
    /// </summary>
    [CommandOption("--tm|--transcriber-model <MODEL>")]
    [Description("Transcription model")]
    public string? TranscriberModel { get; init; } = "base";

    // ----- 音频预处理模块 -----
    /// <summary>
    /// 是否启用条件式 FFmpeg 降噪。
    /// </summary>
    [CommandOption("--audio-noise-reduction")]
    [Description("Enable conditional FFmpeg noise reduction")]
    public bool EnableAudioNoiseReduction { get; init; } = false;

    /// <summary>
    /// 低于此信噪比（dB）时启用降噪，默认 15 dB。
    /// </summary>
    [CommandOption("--audio-snr-threshold <DB>")]
    [Description("Enable noise reduction below this SNR threshold, default 15 dB")]
    public double AudioSnrThresholdDb { get; init; } = 15.0;

    /// <summary>
    /// 是否禁用音频重采样（输出仍会强制转为 16 kHz）。
    /// </summary>
    [CommandOption("--disable-audio-resampling")]
    [Description("Disable audio resampling (output is still forced to 16 kHz)")]
    public bool DisableAudioResampling { get; init; }

    /// <summary>
    /// 是否禁用 100 Hz 高通滤波器。
    /// </summary>
    [CommandOption("--disable-audio-highpass")]
    [Description("Disable the 100 Hz high-pass filter")]
    public bool DisableAudioHighPass { get; init; }

    /// <summary>
    /// 是否禁用 EBU R128 响度归一化。
    /// </summary>
    [CommandOption("--disable-audio-loudness")]
    [Description("Disable EBU R128 loudness normalization")]
    public bool DisableAudioLoudness { get; init; }

    // ----- 人声分离模块 (Vocal Separation) -----
    /// <summary>
    /// 是否在转录前用 Demucs 分离人声（仅适用于音乐/背景音乐较重的素材）。
    /// </summary>
    [CommandOption("--vs|--vocal-separation")]
    [Description("Separate vocals with Demucs before transcription (only for music/BGM-heavy media)")]
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
    /// 是否启用强制对齐。
    /// </summary>
    [CommandOption("-a|--align")]
    [Description("Enable forced alignment")]
    public bool EnableAlignment { get; init; } = true;

    /// <summary>
    /// 强制对齐使用的模型名称。
    /// </summary>
    [CommandOption("--am|--alignment-model <MODEL>")]
    [Description("Model for forced alignment")]
    public string? AlignmentModel { get; init; } = "qwen3-forced-aligner-0.6b-f16";

    /// <summary>
    /// 强制对齐分段：相邻句子的时间间隙超过该秒数时切分为独立块（默认 2.0 秒）。
    /// 每块仅启动一次对齐进程，长音频下显著提速。
    /// </summary>
    [CommandOption("--align-chunk-gap <SEC>")]
    [Description("Split alignment into chunks when the gap between sentences exceeds this many seconds, default 2.0")]
    public double AlignmentChunkGapSeconds { get; init; } = 2.0;

    /// <summary>
    /// 强制对齐分段：单个块的最大音频时长（秒），默认 120 秒。
    /// </summary>
    [CommandOption("--align-max-chunk <SEC>")]
    [Description("Maximum audio duration per alignment chunk in seconds, default 120")]
    public double AlignmentMaxChunkSeconds { get; init; } = 120.0;


    /// <summary>
    /// 字幕显示时允许的最大每秒字符数。
    /// </summary>
    [CommandOption("--max-cps <CPS>")]
    [Description("Maximum displayed characters per second")]
    public double MaxCps { get; init; } = 5.0;

    /// <summary>
    /// 单行字幕允许的最大字符数。
    /// </summary>
    [CommandOption("--max-chars-per-line <CHARS>")]
    [Description("Maximum characters per subtitle line")]
    public int MaxCharsPerLine { get; init; } = 18;

    /// <summary>
    /// 脚本对音频覆盖率的告警阈值（0–1）。
    /// </summary>
    [CommandOption("--coverage-threshold <RATIO>")]
    [Description("Warning threshold for script-to-audio coverage")]
    public double CoverageThreshold { get; init; } = 0.92;

    /// <summary>
    /// 是否将脚本中缺失的词渲染为省略号占位。
    /// </summary>
    [CommandOption("--fill-gap")]
    [Description("Render missing script words as an ellipsis")]
    public bool FillGapWithEllipsis { get; init; } = true;

    /// <summary>
    /// 是否在字幕文本前显示说话人标签；默认开启，传 --no-speaker-labels 关闭。
    /// 说话人信息始终写入 ASS 的 Name 字段，不受本开关影响。
    /// </summary>
    [CommandOption("--no-speaker-labels")]
    [Description("Do not prefix subtitle text with speaker labels (Name field is always written)")]
    public bool ShowSpeakerLabels { get; init; } = true;

}