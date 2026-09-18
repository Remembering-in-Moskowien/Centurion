using System.ComponentModel;
using Spectre.Console.Cli;

namespace Centurion.Cli.Commands.Settings;

public sealed class CorrectSettings : CommandSettings
{
    [CommandArgument(0, "<SUBTITLE_FILE>")]
    [Description("Input subtitle file")]
    public required FileInfo SubtitleFile { get; init; }

    [CommandOption("-o|--output <OUTPUT_FILE>")]
    [Description("Output ASS subtitle file")]
    public FileInfo? OutputFile { get; init; }

    [CommandOption("--audio <AUDIO_FILE>")]
    [Description("Audio file used for timeline correction")]
    public FileInfo? AudioFile { get; init; }

    [CommandOption("--script <SCRIPT_FILE>")]
    [Description("Script file used for text correction")]
    public FileInfo? ScriptFile { get; init; }

    [CommandOption("-s|--strategy <MODE>")]
    [Description("Correction strategy: timeline-only, text-only, both")]
    public string Strategy { get; init; } = "both";

    [CommandOption("--max-drift <MS>")]
    [Description("Maximum drift used for reporting")]
    public int MaxDrift { get; init; } = 1500;

    [CommandOption("--fuzzy-threshold <T>")]
    [Description("Minimum text similarity from 0 to 1")]
    public double FuzzyThreshold { get; init; } = 0.72;

    [CommandOption("--disable-audio-resampling")]
    [Description("Disable audio resampling")]
    public bool DisableAudioResampling { get; init; }

    [CommandOption("--disable-audio-highpass")]
    [Description("Disable the high-pass filter")]
    public bool DisableAudioHighPass { get; init; }

    [CommandOption("--disable-audio-loudness")]
    [Description("Disable loudness normalization")]
    public bool DisableAudioLoudness { get; init; }

    [CommandOption("-k|--karaoke")]
    [Description("Generate ASS karaoke effects")]
    public bool Karaoke { get; init; }
}