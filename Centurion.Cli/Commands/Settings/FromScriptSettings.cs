using System.ComponentModel;
using Centurion.Core.Abstractions;
using Spectre.Console.Cli;

namespace Centurion.Cli.Commands.Settings;

public sealed class FromScriptSettings : CommandSettings
{
    [CommandArgument(0, "<INPUT_FILE>")]
    [Description("Input media file")]
    public required FileInfo InputFile { get; init; }

    [CommandArgument(1, "<SCRIPT_FILE>")]
    [Description("Plain-text script file")]
    public required FileInfo ScriptFile { get; init; }

    [CommandOption("-o|--output <OUTPUT_FILE>")]
    [Description("Output ASS subtitle file")]
    public FileInfo? OutputFile { get; init; }

    [CommandOption("-l|--language <LANG>")]
    [Description("Audio language code, default en")]
    public string Language { get; init; } = "en";

    [CommandOption("-t|--transcriber <ENGINE>")]
    [Description("Transcription engine")]
    public string Transcriber { get; init; } = "whisper";

    [CommandOption("--tm|--transcriber-model <MODEL>")]
    [Description("Transcription model")]
    public string? TranscriberModel { get; init; } = "base";

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

    [CommandOption("-a|--align")]
    [Description("Enable forced alignment")]
    public bool EnableAlignment { get; init; } = true;
    
    [CommandOption("--am|--alignment-model <MODEL>")]
    [Description("Model for forced alignment")]
    public string? AlignmentModel { get; init; } = "qwen3-forced-aligner-0.6b-f16";

    [CommandOption("--max-cps <CPS>")]
    [Description("Maximum displayed characters per second")]
    public double MaxCps { get; init; } = 5.0;

    [CommandOption("--max-chars-per-line <CHARS>")]
    [Description("Maximum characters per subtitle line")]
    public int MaxCharsPerLine { get; init; } = 18;

    [CommandOption("--coverage-threshold <RATIO>")]
    [Description("Warning threshold for script-to-audio coverage")]
    public double CoverageThreshold { get; init; } = 0.92;

    [CommandOption("--fill-gap")]
    [Description("Render missing script words as an ellipsis")]
    public bool FillGapWithEllipsis { get; init; } = true;
}