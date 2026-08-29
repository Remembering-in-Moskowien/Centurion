using System.ComponentModel;
using Spectre.Console.Cli;

namespace Centurion.Cli.Commands.Settings;

public sealed class SpawnSettings : CommandSettings
{
    // ----- 基础参数 -----
    [CommandArgument(0, "<INPUT_FILE>")]
    [Description("Input media file")]
    public required FileInfo InputFile { get; init; }

    [CommandOption("-o|--output <OUTPUT_FILE>")]
    [Description("Output ASS subtitle file")]
    public FileInfo? OutputFile { get; init; }

    [CommandOption("--language <LANG>")]
    [Description("Audio language code, default en")]
    public string Language { get; init; } = "en";

    [CommandOption("--num-speakers <NUM>")]
    [Description("Number of speakers (0 = auto detect), default 0")]
    public int NumSpeakers { get; init; } = 0;

    [CommandOption("--karaoke")]
    [Description("Generate ASS subtitles with karaoke effects (\\K tags)")]
    public bool Karaoke { get; init; }

    // ----- 转录模块 (Transcriber) -----
    [CommandOption("--transcriber <ENGINE>")]
    [Description("Transcription engine: whisper, qwen, api")]
    public string Transcriber { get; init; } = "whisper";

    [CommandOption("--transcriber-model <MODEL>")]
    [Description("Model name for the transcriber (e.g., base, large, qwen-asr-1.0)")]
    public string? TranscriberModel { get; init; }

    [CommandOption("--transcriber-prompt <PROMPT>")]
    [Description("Initial prompt for transcription")]
    public string? InitialPrompt { get; init; }

    // ----- 分句模块 (Splitter) -----
    [CommandOption("--splitter <STRATEGY>")]
    [Description("Split strategy: heuristic, llm, rule")]
    public string Splitter { get; init; } = "heuristic";

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

    // ----- 对齐模块 (Aligner) -----
    [CommandOption("--aligner <ENGINE>")]
    [Description("Alignment engine: qwen, gentle (omit to disable)")]
    public string? Aligner { get; init; }

    [CommandOption("--aligner-model <MODEL>")]
    [Description("Model for alignment (e.g., qwen-align-1.0)")]
    public string? AlignerModel { get; init; }
}