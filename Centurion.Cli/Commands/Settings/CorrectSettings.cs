using Centurion.Models.Workflow;
using System.ComponentModel;
using Centurion.Abstractions;
using Spectre.Console.Cli;

namespace Centurion.Cli.Commands.Settings;

/// <summary>
/// Options for the <c>correct</c> command: fix existing subtitles against source audio
/// and/or a reference script (input is a Centurion intermediate file).
/// </summary>
public sealed class CorrectSettings : GlobalCommandSettings
{

    /// <summary>
    /// Centurion intermediate file to correct (.centurion.json).
    /// </summary>
    [CommandArgument(0, "<CENTURION_FILE>")]
    [Description("Centurion intermediate file (.centurion.json)")]
    public required FileInfo CenturionFile { get; init; }

    /// <summary>
    /// Output intermediate file; defaults to {input}.corrected.centurion.json.
    /// </summary>
    [CommandOption("-o|--output <OUTPUT_FILE>")]
    [Description("Output Centurion intermediate file (default: {input}.corrected.centurion.json)")]
    public FileInfo? OutputFile { get; init; }

    /// <summary>
    /// Audio/media file used for timeline correction (required for timeline strategies).
    /// </summary>
    [CommandOption("--audio <AUDIO_FILE>")]
    [Description("Audio/media file used for timeline correction (required for timeline strategies)")]
    public FileInfo? AudioFile { get; init; }

    /// <summary>
    /// Reference script file used for text correction (required for text strategies).
    /// </summary>
    [CommandOption("--script <SCRIPT_FILE>")]
    [Description("Script file used for text correction")]
    public FileInfo? ScriptFile { get; init; }

    /// <summary>
    /// Correction strategy: timeline-only, text-only, both.
    /// </summary>
    [CommandOption("-s|--strategy <MODE>")]
    [Description("Correction strategy: timeline-only, text-only, both")]
    public string Strategy { get; init; } = "both";

    /// <summary>
    /// Maximum allowed drift used for reporting (ms).
    /// </summary>
    [CommandOption("--max-drift <MS>")]
    [Description("Maximum drift used for reporting")]
    public int MaxDrift { get; init; } = 1500;

    /// <summary>
    /// Minimum text similarity threshold for fuzzy matching (0–1).
    /// </summary>
    [CommandOption("--fuzzy-threshold <T>")]
    [Description("Minimum text similarity from 0 to 1")]
    public double FuzzyThreshold { get; init; } = 0.72;

    /// <summary>
    /// Whether to disable audio resampling.
    /// </summary>
    [CommandOption("--disable-audio-resampling")]
    [Description("Disable audio resampling")]
    public bool DisableAudioResampling { get; init; }

    /// <summary>
    /// Whether to disable the high-pass filter.
    /// </summary>
    [CommandOption("--disable-audio-highpass")]
    [Description("Disable the high-pass filter")]
    public bool DisableAudioHighPass { get; init; }

    /// <summary>
    /// Whether to disable loudness normalization.
    /// </summary>
    [CommandOption("--disable-audio-loudness")]
    [Description("Disable loudness normalization")]
    public bool DisableAudioLoudness { get; init; }

    // ----- 人声分离模块 (Vocal Separation) -----
    /// <summary>
    /// Whether to separate vocals with Demucs before alignment (only for music/BGM-heavy media).
    /// </summary>
    [CommandOption("--vs|--vocal-separation")]
    [Description("Separate vocals with Demucs before alignment (only for music/BGM-heavy media)")]
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
    /// Audio language code (e.g. en, zh), default en; pass zh for Chinese audio for more stable transcription.
    /// </summary>
    [CommandOption("-l|--language <LANG>")]
    [Description("Audio language code, default en")]
    public string Language { get; init; } = "en";

    /// <summary>
    /// Whether to generate ASS karaoke per-word effects.
    /// </summary>
    [CommandOption("-k|--karaoke")]
    [Description("Generate ASS karaoke effects")]
    public bool Karaoke { get; init; }

    /// <summary>
    /// Whether to prefix subtitle text with speaker labels (default on; pass --no-speaker-labels to disable).
    /// Speaker info is always written to the ASS Name field regardless of this switch.
    /// </summary>
    [CommandOption("--no-speaker-labels")]
    [Description("Do not prefix subtitle text with speaker labels (Name field is always written)")]
    public bool ShowSpeakerLabels { get; init; } = true;

    /// <summary>
    /// Whether to run Hunspell spell checking (detects suspicious words and reports them).
    /// </summary>
    [CommandOption("--spellcheck")]
    [Description("Check subtitle text with Hunspell and report suspicious words")]
    public bool SpellCheck { get; init; }

    /// <summary>
    /// Hunspell dictionary prefix, default en_US (auto-downloaded when missing; only the default prefix is supported).
    /// </summary>
    [CommandOption("--hunspell-dict <PREFIX>")]
    [Description("Hunspell dictionary prefix, default en_US (auto-downloaded if missing)")]
    public string HunspellDictionary { get; init; } = "en_US";

}