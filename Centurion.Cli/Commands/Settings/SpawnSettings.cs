// Centurion.Cli/Commands/Settings/SpawnSettings.cs
using System.ComponentModel;
using Centurion.Core.Abstractions;
using Spectre.Console.Cli;

namespace Centurion.Cli.Commands.Settings;

public sealed class SpawnSettings : CommandSettings
{
    // ----- 基础参数（不变）-----
    [CommandArgument(0, "<INPUT_FILE>")]
    [Description("Input media file")]
    public required FileInfo InputFile { get; init; }

    [CommandOption("-o|--output <OUTPUT_FILE>")]
    [Description("Output ASS subtitle file")]
    public FileInfo? OutputFile { get; init; }

    [CommandOption("-l|--language <LANG>")]
    [Description("Audio language code, default en")]
    public string Language { get; init; } = "en";

    [CommandOption("--num-speakers <NUM>")]
    [Description("Number of speakers (0 = auto detect), default 0")]
    public int NumSpeakers { get; init; } = 0;

    [CommandOption("-k|--karaoke")]
    [Description("Generate ASS subtitles with karaoke effects (\\K tags)")]
    public bool Karaoke { get; init; } = false;

    // ----- 转录模块 (Transcriber) -----
    [CommandOption("-t|--transcriber <ENGINE>")]
    [Description("Transcription engine: whisper, crisp")]
    public string Transcriber { get; init; } = "crispasr";

    [CommandOption("--tm|--transcriber-model <MODEL>")]
    [Description("Model name for the transcriber (e.g., base, large, qwen-asr-1.0)")]
    public string? TranscriberModel { get; init; } = "qwen3-asr-1.7b";

    [CommandOption("--tp|--transcriber-prompt <PROMPT>")]
    [Description("Initial prompt for transcription")]
    public string? InitialPrompt { get; init; }

    // ----- 音频预处理模块 -----
    [CommandOption("--audio-noise-reduction")]
    [Description("Enable conditional FFmpeg noise reduction")]
    public bool EnableAudioNoiseReduction { get; init; } = false;

    [CommandOption("--audio-snr-threshold <DB>")]
    [Description("Enable noise reduction below this SNR threshold, default 15 dB")]
    public double AudioSnrThresholdDb { get; init; } = 15.0;

    [CommandOption("--disable-audio-resampling")]
    [Description("Disable audio resampling (output is still forced to 16 kHz)")]
    public bool DisableAudioResampling { get; init; }

    [CommandOption("--disable-audio-highpass")]
    [Description("Disable the 100 Hz high-pass filter")]
    public bool DisableAudioHighPass { get; init; }

    [CommandOption("--disable-audio-loudness")]
    [Description("Disable EBU R128 loudness normalization")]
    public bool DisableAudioLoudness { get; init; }

    // ----- 人声分离模块 (Vocal Separation) -----
    [CommandOption("--vocal-separation")]
    [Description("Separate vocals with Demucs before transcription (only for music/BGM-heavy media)")]
    public bool VocalSeparation { get; init; } = false;

    [CommandOption("--vocal-separation-model <MODEL>")]
    [Description("Demucs model name, default htdemucs")]
    public string VocalSeparationModel { get; init; } = "htdemucs";

    // ----- 推理设备 (Device) -----
    [CommandOption("--device <DEVICE>")]
    [Description("Inference device: auto, cpu, cuda, vulkan, directml (GPU tool builds auto-downloaded)")]
    public InferenceDevice Device { get; init; } = InferenceDevice.Auto;

    // ----- 分句模块 (Splitter) -----
    [CommandOption("-s|--splitter <STRATEGY>")]
    [Description("Split strategy: rule, llm")]
    public string Splitter { get; init; } = "rule";

    [CommandOption("--splitter-chunk-granularity <LEVEL>")]
    [Description("NLP chunk granularity from 0.0 to 1.0, default 0.5")]
    public float ChunkGranularity { get; init; } = 0.5f;

    [CommandOption("--splitter-target-length <CHARS>")]
    [Description("Target characters per line, default 50")]
    public int TargetLength { get; init; } = 50;

    [CommandOption("--splitter-max-length <CHARS>")]
    [Description("Maximum characters per line, default 80")]
    public int MaxLength { get; init; } = 80;

    [CommandOption("--splitter-spread <RANGE>")]
    [Description("Spread range for line length distribution, default 10")]
    public int SpreadRange { get; init; } = 10;

    [CommandOption("--splitter-model <MODEL>")]
    [Description("Model for LLM-based splitting (e.g., gpt-4)")]
    public string? SplitterModel { get; init; }

    [CommandOption("--splitter-api-key <KEY>")]
    [Description("API key for LLM splitter")]
    public string? SplitterApiKey { get; init; }

    // ----- 对齐模块 (Alignment) -----
    [CommandOption("-a|--align")]
    [Description("Enable forced alignment")]
    public bool EnableAlignment { get; init; } = true;

    [CommandOption("--am|--alignment-model <MODEL>")]
    [Description("Model for forced alignment")]
    public string? AlignmentModel { get; init; } = "qwen3-forced-aligner-0.6b-f16";
}