using Centurion.Models.Workflow;
using System.ComponentModel;
using Centurion.Abstractions;
using Spectre.Console.Cli;

namespace Centurion.Cli.Commands.Settings;

/// <summary>
/// Options for the <c>asr</c> command: transcribe, diarize, split and align
/// audio/video media into subtitles.
/// </summary>
public sealed class SpawnSettings : GlobalCommandSettings
{

    // ----- 基础参数（不变）-----
    /// <summary>
    /// Input audio/video media file.
    /// </summary>
    [CommandArgument(0, "<INPUT_FILE>")]
    [Description("Input media file")]
    public required FileInfo InputFile { get; init; }

    /// <summary>
    /// Output IR intermediate file; defaults to {input}.asr.centurion.json.
    /// </summary>
    [CommandOption("-o|--output <OUTPUT_FILE>")]
    [Description("Output IR intermediate file")]
    public FileInfo? OutputFile { get; init; }

    /// <summary>
    /// Audio language code, default en.
    /// </summary>
    [CommandOption("-l|--language <LANG>")]
    [Description("Audio language code, default en")]
    public string Language { get; init; } = "en";

    /// <summary>
    /// Number of speakers; 0 means auto-detect, default 0.
    /// </summary>
    [CommandOption("--num-speakers <NUM>")]
    [Description("Number of speakers (0 = auto detect), default 0")]
    public int NumSpeakers { get; init; } = 0;

    /// <summary>
    /// Whether to enable speaker diarization (default off; uses crispasr when no backend is given).
    /// </summary>
    [CommandOption("-d|--diarize")]
    [Description("Enable speaker diarization (default backend: crispasr)")]
    public bool Diarize { get; init; } = false;

    /// <summary>
    /// Diarization backend: crispasr (built-in) or pyannote (Pyannote + TitaNet).
    /// Specifying a backend enables diarization automatically.
    /// </summary>
    [CommandOption("--diarization-backend <BACKEND>")]
    [Description("Diarization backend: crispasr, pyannote (implies --diarize)")]
    public string? DiarizationBackend { get; init; }

    /// <summary>
    /// Whether to generate ASS subtitles with karaoke per-word effects (\K tags).
    /// </summary>
    [CommandOption("-k|--karaoke")]
    [Description("Generate ASS subtitles with karaoke effects (\\K tags)")]
    public bool Karaoke { get; init; } = false;

    /// <summary>
    /// Whether to prefix subtitle text with speaker labels (default on; pass --no-speaker-labels to disable).
    /// Speaker info is always written to the ASS Name field regardless of this switch.
    /// </summary>
    [CommandOption("--no-speaker-labels")]
    [Description("Do not prefix subtitle text with speaker labels (Name field is always written)")]
    public bool ShowSpeakerLabels { get; init; } = true;


    // ----- 转录模块 (Transcriber) -----
    /// <summary>
    /// Transcription engine: whisper, crisp.
    /// </summary>
    [CommandOption("-t|--transcriber <ENGINE>")]
    [Description("Transcription engine: whisper, crisp")]
    public string Transcriber { get; init; } = "crispasr";

    /// <summary>
    /// Transcription model name (e.g. base, large, qwen-asr-1.0).
    /// </summary>
    [CommandOption("--tm|--transcriber-model <MODEL>")]
    [Description("Model name for the transcriber (e.g., base, large, qwen-asr-1.0)")]
    public string? TranscriberModel { get; init; } = "qwen3-asr-1.7b";

    /// <summary>
    /// Initial prompt used for transcription.
    /// </summary>
    [CommandOption("--tp|--transcriber-prompt <PROMPT>")]
    [Description("Initial prompt for transcription")]
    public string? InitialPrompt { get; init; }

    // ----- 音频预处理模块 -----
    /// <summary>
    /// Whether to enable conditional FFmpeg noise reduction.
    /// </summary>
    [CommandOption("--audio-noise-reduction")]
    [Description("Enable conditional FFmpeg noise reduction")]
    public bool EnableAudioNoiseReduction { get; init; } = false;

    /// <summary>
    /// Enable noise reduction below this SNR (dB), default 15 dB.
    /// </summary>
    [CommandOption("--audio-snr-threshold <DB>")]
    [Description("Enable noise reduction below this SNR threshold, default 15 dB")]
    public double AudioSnrThresholdDb { get; init; } = 15.0;

    /// <summary>
    /// Whether to disable audio resampling (output is still forced to 16 kHz).
    /// </summary>
    [CommandOption("--disable-audio-resampling")]
    [Description("Disable audio resampling (output is still forced to 16 kHz)")]
    public bool DisableAudioResampling { get; init; }

    /// <summary>
    /// Whether to disable the 100 Hz high-pass filter.
    /// </summary>
    [CommandOption("--disable-audio-highpass")]
    [Description("Disable the 100 Hz high-pass filter")]
    public bool DisableAudioHighPass { get; init; }

    /// <summary>
    /// Whether to disable EBU R128 loudness normalization.
    /// </summary>
    [CommandOption("--disable-audio-loudness")]
    [Description("Disable EBU R128 loudness normalization")]
    public bool DisableAudioLoudness { get; init; }

    // ----- 人声分离模块 (Vocal Separation) -----
    /// <summary>
    /// Whether to separate vocals with Demucs before transcription (only for music/BGM-heavy media).
    /// </summary>
    [CommandOption("--vs|--vocal-separation")]
    [Description("Separate vocals with Demucs before transcription (only for music/BGM-heavy media)")]
    public bool VocalSeparation { get; init; } = false;

    /// <summary>
    /// Demucs vocal separation model name, default htdemucs.
    /// </summary>
    [CommandOption("--vsm|--vocal-separation-model <MODEL>")]
    [Description("Demucs model name, default htdemucs")]
    public string VocalSeparationModel { get; init; } = "htdemucs";

    // ----- VAD 过滤 (Voice Activity Filter) -----
    /// <summary>
    /// Disable VAD pre-filtering: speech segments are aggregated (instrumental/silence dropped)
    /// before vocal separation/transcription, then timestamps are mapped back to the source timeline.
    /// </summary>
    [CommandOption("--no-vad")]
    [Description("Disable VAD pre-filtering (aggregate speech before separation/transcription)")]
    public bool DisableVadFilter { get; init; } = false;

    /// <summary>
    /// VAD energy threshold ratio: a window whose RMS is below the global mean times this ratio
    /// is treated as non-speech (instrumental/silence). Default 0.2; raise it to filter more.
    /// </summary>
    [CommandOption("--vad-threshold <RATIO>")]
    [Description("VAD energy threshold ratio (window RMS < mean*r is non-speech), default 0.2")]
    public double VadEnergyThresholdRatio { get; init; } = 0.2;

    // ----- 推理设备 (Device) -----
    /// <summary>
    /// Inference device preference: auto, cpu, cuda, vulkan, directml (GPU tool builds auto-downloaded).
    /// </summary>
    [CommandOption("--device <DEVICE>")]
    [Description("Inference device: auto, cpu, cuda, vulkan, directml (GPU tool builds auto-downloaded)")]
    public InferenceDevice Device { get; init; } = InferenceDevice.Auto;

    // ----- 云端 ASR (Cloud ASR API) -----
    /// <summary>
    /// Cloud ASR provider: openai / groq / dashscope / deepgram (pass the matching engine to --transcriber).
    /// </summary>
    [CommandOption("--asr-provider <PROVIDER>")]
    [Description("Cloud ASR provider: openai / groq / dashscope / deepgram (use with -t openai etc.)")]
    public string AsrProvider { get; init; } = "crispasr";

    /// <summary>
    /// Cloud ASR API key.
    /// </summary>
    [CommandOption("--asr-api-key <KEY>")]
    [Description("Cloud ASR API key")]
    public string? AsrApiKey { get; init; }

    /// <summary>
    /// Cloud ASR endpoint; defaults per provider when omitted.
    /// </summary>
    [CommandOption("--asr-base-url <URL>")]
    [Description("Cloud ASR endpoint (defaults per provider)")]
    public string? AsrBaseUrl { get; init; }

    // ----- 分句模块 (Splitter) -----
    /// <summary>
    /// Split strategy: rule/rule-aggressive (active rules, short dialogue; default), rule-passive (passive rules, steady speech), llm (LLM-based).
    /// </summary>
    [CommandOption("-s|--splitter <STRATEGY>")]
    [Description("Split strategy: rule (default) / rule-passive / llm")]
    public string Splitter { get; init; } = "rule";

    /// <summary>
    /// NLP chunk granularity (0.0–1.0, higher splits finer), default 0.5.
    /// </summary>
    [CommandOption("--splitter-chunk-granularity <LEVEL>")]
    [Description("NLP chunk granularity from 0.0 to 1.0, default 0.5")]
    public float ChunkGranularity { get; init; } = 0.5f;

    /// <summary>
    /// Target characters per line, default 50.
    /// </summary>
    [CommandOption("--splitter-target-length <CHARS>")]
    [Description("Target characters per line, default 50")]
    public int TargetLength { get; init; } = 50;

    /// <summary>
    /// Maximum characters per line, default 80.
    /// </summary>
    [CommandOption("--splitter-max-length <CHARS>")]
    [Description("Maximum characters per line, default 80")]
    public int MaxLength { get; init; } = 80;

    /// <summary>
    /// Spread range for line length distribution, default 10.
    /// </summary>
    [CommandOption("--splitter-spread <RANGE>")]
    [Description("Spread range for line length distribution, default 10")]
    public int SpreadRange { get; init; } = 10;

    /// <summary>
    /// Model used for LLM-based splitting (e.g. gpt-4).
    /// </summary>
    [CommandOption("--splitter-model <MODEL>")]
    [Description("Model for LLM-based splitting (e.g., gpt-4)")]
    public string? SplitterModel { get; init; }

    /// <summary>
    /// API key for the LLM splitter model.
    /// </summary>
    [CommandOption("--splitter-api-key <KEY>")]
    [Description("API key for LLM splitter")]
    public string? SplitterApiKey { get; init; }

    /// <summary>
    /// LLM provider used by the splitter (openai/deepseek/moonshot/zhipu/openrouter/groq/siliconflow/dashscope/ark/azure/ollama).
    /// </summary>
    [CommandOption("--llm-provider <PROVIDER>")]
    [Description("LLM provider for splitting: openai, deepseek, moonshot, zhipu, openrouter, groq, ollama, ...")]
    public string? LlmProvider { get; init; }

    /// <summary>
    /// Custom LLM splitter endpoint; uses the provider default when empty.
    /// </summary>
    [CommandOption("--llm-base-url <URL>")]
    [Description("LLM base URL for splitting (defaults to provider endpoint)")]
    public string? LlmBaseUrl { get; init; }

    // ----- 对齐模块 (Alignment) -----
    /// <summary>
    /// Whether to enable forced alignment.
    /// </summary>
    [CommandOption("-a|--align")]
    [Description("Enable forced alignment")]
    public bool EnableAlignment { get; init; } = true;

    /// <summary>
    /// Model used for forced alignment.
    /// </summary>
    [CommandOption("--am|--alignment-model <MODEL>")]
    [Description("Model for forced alignment")]
    public string? AlignmentModel { get; init; } = "qwen3-forced-aligner-0.6b-f16";

    /// <summary>
    /// Alignment chunking: split into independent blocks when the gap between sentences exceeds this many seconds (default 2.0).
    /// Each block starts one alignment pass, speeding up long audio.
    /// </summary>
    [CommandOption("--align-chunk-gap <SEC>")]
    [Description("Split alignment into chunks when the gap between sentences exceeds this many seconds, default 2.0")]
    public double AlignmentChunkGapSeconds { get; init; } = 2.0;

    /// <summary>
    /// Alignment chunking: maximum audio duration per block in seconds, default 120.
    /// </summary>
    [CommandOption("--align-max-chunk <SEC>")]
    [Description("Maximum audio duration per alignment chunk in seconds, default 120")]
    public double AlignmentMaxChunkSeconds { get; init; } = 120.0;

}