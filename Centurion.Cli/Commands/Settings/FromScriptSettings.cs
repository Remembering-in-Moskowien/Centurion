using Centurion.Models.Workflow;
using System.ComponentModel;
using Centurion.Abstractions;
using Spectre.Console.Cli;

namespace Centurion.Cli.Commands.Settings;

/// <summary>
/// Options for the <c>from-script</c> command: align a plain-text script to audio/video
/// and produce timestamped subtitles.
/// </summary>
public sealed class FromScriptSettings : GlobalCommandSettings
{

    /// <summary>
    /// Input audio/video media file.
    /// </summary>
    [CommandArgument(0, "<INPUT_FILE>")]
    [Description("Input media file")]
    public required FileInfo InputFile { get; init; }

    /// <summary>
    /// Plain-text script file.
    /// </summary>
    [CommandArgument(1, "<SCRIPT_FILE>")]
    [Description("Plain-text script file")]
    public required FileInfo ScriptFile { get; init; }

    /// <summary>
    /// Output IR intermediate file; defaults to {input}.centurion.json.
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
    /// Transcription engine name.
    /// </summary>
    [CommandOption("-t|--transcriber <ENGINE>")]
    [Description("Transcription engine")]
    public string Transcriber { get; init; } = "whisper";

    /// <summary>
    /// Transcription model name.
    /// </summary>
    [CommandOption("--tm|--transcriber-model <MODEL>")]
    [Description("Transcription model")]
    public string? TranscriberModel { get; init; } = "base";

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
    [CommandOption("--vocal-separation-model <MODEL>")]
    [Description("Demucs model name, default htdemucs")]
    public string VocalSeparationModel { get; init; } = "htdemucs";

    // ----- VAD 过滤 (Voice Activity Filter) -----
    /// <summary>
    /// Disable VAD pre-filtering (aggregate speech before vocal separation/transcription).
    /// </summary>
    [CommandOption("--no-vad")]
    [Description("Disable VAD pre-filtering (aggregate speech before separation/transcription)")]
    public bool DisableVadFilter { get; init; } = false;

    /// <summary>
    /// VAD energy threshold ratio (window RMS below mean*r is non-speech), default 0.2.
    /// </summary>
    [CommandOption("--vad-threshold <RATIO>")]
    [Description("VAD energy threshold ratio (window RMS below mean*r is non-speech), default 0.2")]
    public double VadEnergyThresholdRatio { get; init; } = 0.2;

    // ----- 推理设备 (Device) -----
    /// <summary>
    /// Inference device preference: auto, cpu, cuda, vulkan, directml (GPU tool builds auto-downloaded).
    /// </summary>
    [CommandOption("--device <DEVICE>")]
    [Description("Inference device: auto, cpu, cuda, vulkan, directml (GPU tool builds auto-downloaded)")]
    public InferenceDevice Device { get; init; } = InferenceDevice.Auto;

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


    /// <summary>
    /// Maximum displayed characters per second.
    /// </summary>
    [CommandOption("--max-cps <CPS>")]
    [Description("Maximum displayed characters per second")]
    public double MaxCps { get; init; } = 5.0;

    /// <summary>
    /// Maximum characters per subtitle line.
    /// </summary>
    [CommandOption("--max-chars-per-line <CHARS>")]
    [Description("Maximum characters per subtitle line")]
    public int MaxCharsPerLine { get; init; } = 18;

    /// <summary>
    /// Warning threshold for script-to-audio coverage (0–1).
    /// </summary>
    [CommandOption("--coverage-threshold <RATIO>")]
    [Description("Warning threshold for script-to-audio coverage")]
    public double CoverageThreshold { get; init; } = 0.92;

    /// <summary>
    /// Whether to render missing script words as an ellipsis placeholder.
    /// </summary>
    [CommandOption("--fill-gap")]
    [Description("Render missing script words as an ellipsis")]
    public bool FillGapWithEllipsis { get; init; } = true;

    /// <summary>
    /// Whether to prefix subtitle text with speaker labels (default on; pass --no-speaker-labels to disable).
    /// Speaker info is always written to the ASS Name field regardless of this switch.
    /// </summary>
    [CommandOption("--no-speaker-labels")]
    [Description("Do not prefix subtitle text with speaker labels (Name field is always written)")]
    public bool ShowSpeakerLabels { get; init; } = true;

}