using Spectre.Console.Cli;
using System.ComponentModel;

namespace Centurion.Cli.Commands.Settings;

/// <summary>
/// Settings for the <c>dub</c> command: media dubbing (Centurion intermediate file
/// → dubbed wav + intermediate file). Phase 2/3: background mixing (ducking),
/// loudness normalization, parallel TTS, long-sentence chunking, overlap fallback.
/// </summary>
public sealed class DubSettings : GlobalCommandSettings
{

    /// <summary>Input Centurion intermediate file (sentences, translations and speaker info).</summary>
    [CommandArgument(0, "<CENTURION_FILE>")]
    [Description("Centurion intermediate file (.centurion.json)")]
    public required FileInfo CenturionFile { get; set; }

    /// <summary>Optional: original media file (speaker reference audio extraction; also the input duration baseline).</summary>
    [CommandOption("--media <FILE>")]
    [Description("Original media file (speaker reference extraction; duration baseline)")]
    public FileInfo? MediaFile { get; set; }

    /// <summary>Optional: speaker reference audio directory (one wav per speaker, named SPEAKER_xx.wav).</summary>
    [CommandOption("--speaker-reference <DIR>")]
    [Description("Speaker reference audio directory (one wav per speaker, SPEAKER_xx.wav)")]
    public DirectoryInfo? SpeakerReference { get; set; }

    /// <summary>Dubbing target language (ISO 639-1, e.g. zh/en/ja/de).</summary>
    [CommandOption("--target-language <LANG>")]
    [Description("Dubbing target language (ISO 639-1, e.g. zh/en/ja/de)")]
    public string TargetLanguage { get; set; } = "zh";

    /// <summary>TTS engine (currently "llama").</summary>
    [CommandOption("--tts-engine <ENGINE>")]
    [Description("TTS engine (currently llama)")]
    public string TtsEngine { get; set; } = "llama";

    /// <summary>TTS model name (a key registered in metadata.json Models, e.g. 1.7b-base-q4).</summary>
    [CommandOption("--tts-model <MODEL>")]
    [Description("TTS model name (key in metadata.json Models, e.g. 1.7b-base-q4)")]
    public string TtsModel { get; set; } = "1.7b-base-q4";

    /// <summary>Output intermediate file; defaults to {input}.dub.centurion.json (wav written as .dub.wav).</summary>
    [CommandOption("-o|--output <FILE>")]
    [Description("Output intermediate file (default: {input}.dub.centurion.json; wav as .dub.wav)")]
    public FileInfo? OutputFile { get; set; }

    /// <summary>Strict time alignment: clamp to bounds outside the 0.5x-2.0x adjustable range (default on; off keeps the raw synthesis duration).</summary>
    [CommandOption("--strict-timing")]
    [Description("Strict time alignment: clamp outside the 0.5x-2.0x range (default on)")]
    public bool StrictTiming { get; set; } = true;

    /// <summary>Optional: background audio (soundtrack or score); enables sidechain ducking mixing when provided.</summary>
    [CommandOption("--background <FILE>")]
    [Description("Background audio (soundtrack/score); enables sidechain ducking")]
    public FileInfo? BackgroundFile { get; set; }

    /// <summary>Disable ducking (enabled by default when --background is set).</summary>
    [CommandOption("--no-ducking")]
    [Description("Disable ducking (enabled by default with --background)")]
    public bool NoDucking { get; set; }

    /// <summary>Output loudness target in LUFS (default -16; used with loudnorm normalization).</summary>
    [CommandOption("--loudness-target <LUFS>")]
    [Description("Output loudness target in LUFS (default -16)")]
    public double LoudnessTarget { get; set; } = -16;

    /// <summary>TTS synthesis parallelism (default 2; set 1 when CPU memory is tight).</summary>
    [CommandOption("--tts-parallelism <N>")]
    [Description("TTS synthesis parallelism (default 2; set 1 when memory is tight)")]
    public int TtsParallelism { get; set; } = 2;

    /// <summary>Long-sentence chunking threshold in seconds (default 15; longer segments are split proportionally).</summary>
    [CommandOption("--max-chunk-seconds <S>")]
    [Description("Long-sentence chunking threshold in seconds (default 15)")]
    public double MaxChunkSeconds { get; set; } = 15;
}
