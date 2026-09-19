using Centurion.Models.Workflow;
using System.ComponentModel;
using Centurion.Abstractions;
using Spectre.Console.Cli;

namespace Centurion.Cli.Commands.Settings;

/// <summary>
/// <c>spawn</c> 命令的选项：从音视频媒体自动完成转录、说话人分割、分句与对齐，生成字幕。
/// </summary>
public sealed class SpawnSettings : CommandSettings
{
    // ----- 基础参数（不变）-----
    /// <summary>
    /// 输入音视频媒体文件。
    /// </summary>
    [CommandArgument(0, "<INPUT_FILE>")]
    [Description("Input media file")]
    public required FileInfo InputFile { get; init; }

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
    /// 说话人数；0 表示自动检测，默认 0。
    /// </summary>
    [CommandOption("--num-speakers <NUM>")]
    [Description("Number of speakers (0 = auto detect), default 0")]
    public int NumSpeakers { get; init; } = 0;

    /// <summary>
    /// 是否生成带卡拉OK逐字效果（\K 标签）的 ASS 字幕。
    /// </summary>
    [CommandOption("-k|--karaoke")]
    [Description("Generate ASS subtitles with karaoke effects (\\K tags)")]
    public bool Karaoke { get; init; } = false;

    // ----- 转录模块 (Transcriber) -----
    /// <summary>
    /// 转录引擎：whisper、crisp。
    /// </summary>
    [CommandOption("-t|--transcriber <ENGINE>")]
    [Description("Transcription engine: whisper, crisp")]
    public string Transcriber { get; init; } = "crispasr";

    /// <summary>
    /// 转录模型名称（如 base、large、qwen-asr-1.0）。
    /// </summary>
    [CommandOption("--tm|--transcriber-model <MODEL>")]
    [Description("Model name for the transcriber (e.g., base, large, qwen-asr-1.0)")]
    public string? TranscriberModel { get; init; } = "qwen3-asr-1.7b";

    /// <summary>
    /// 转录使用的初始提示词。
    /// </summary>
    [CommandOption("--tp|--transcriber-prompt <PROMPT>")]
    [Description("Initial prompt for transcription")]
    public string? InitialPrompt { get; init; }

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
    [CommandOption("--vsm|--vocal-separation-model <MODEL>")]
    [Description("Demucs model name, default htdemucs")]
    public string VocalSeparationModel { get; init; } = "htdemucs";

    // ----- 推理设备 (Device) -----
    /// <summary>
    /// 推理设备偏好：auto、cpu、cuda、vulkan、directml（GPU 工具会自动下载对应变体）。
    /// </summary>
    [CommandOption("--device <DEVICE>")]
    [Description("Inference device: auto, cpu, cuda, vulkan, directml (GPU tool builds auto-downloaded)")]
    public InferenceDevice Device { get; init; } = InferenceDevice.Auto;

    // ----- 分句模块 (Splitter) -----
    /// <summary>
    /// 分句策略：rule（规则）、llm（大模型）。
    /// </summary>
    [CommandOption("-s|--splitter <STRATEGY>")]
    [Description("Split strategy: rule, llm")]
    public string Splitter { get; init; } = "rule";

    /// <summary>
    /// NLP 分块粒度（0.0–1.0，越大切分越细），默认 0.5。
    /// </summary>
    [CommandOption("--splitter-chunk-granularity <LEVEL>")]
    [Description("NLP chunk granularity from 0.0 to 1.0, default 0.5")]
    public float ChunkGranularity { get; init; } = 0.5f;

    /// <summary>
    /// 期望的单行目标字符数，默认 50。
    /// </summary>
    [CommandOption("--splitter-target-length <CHARS>")]
    [Description("Target characters per line, default 50")]
    public int TargetLength { get; init; } = 50;

    /// <summary>
    /// 单行字符数上限，默认 80。
    /// </summary>
    [CommandOption("--splitter-max-length <CHARS>")]
    [Description("Maximum characters per line, default 80")]
    public int MaxLength { get; init; } = 80;

    /// <summary>
    /// 行长度分布的扩散范围，默认 10。
    /// </summary>
    [CommandOption("--splitter-spread <RANGE>")]
    [Description("Spread range for line length distribution, default 10")]
    public int SpreadRange { get; init; } = 10;

    /// <summary>
    /// 基于 LLM 分句时使用的模型名称（如 gpt-4）。
    /// </summary>
    [CommandOption("--splitter-model <MODEL>")]
    [Description("Model for LLM-based splitting (e.g., gpt-4)")]
    public string? SplitterModel { get; init; }

    /// <summary>
    /// LLM 分句模型所需的 API 密钥。
    /// </summary>
    [CommandOption("--splitter-api-key <KEY>")]
    [Description("API key for LLM splitter")]
    public string? SplitterApiKey { get; init; }

    // ----- 对齐模块 (Alignment) -----
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
}