using System.ComponentModel;
using Centurion.Abstractions;
using Spectre.Console.Cli;

namespace Centurion.Cli.Commands.Settings;

/// <summary>Options for the <c>ocr</c> command: OCR backend, frame extraction and output.</summary>
public sealed class OcrSettings : GlobalCommandSettings
{
    /// <summary>Input video or image file.</summary>
    [CommandArgument(0, "<INPUT_FILE>")]
    [Description("Input video or image file")]
    public required FileInfo InputFile { get; init; }

    /// <summary>Output IR intermediate file; defaults to {input}.ocr.centurion.json.</summary>
    [CommandOption("-o|--output <OUTPUT_FILE>")]
    [Description("Output IR intermediate file")]
    public FileInfo? OutputFile { get; init; }

    /// <summary>Subtitle language code.</summary>
    [CommandOption("-l|--language <LANG>")]
    [Description("Subtitle language code, default en")]
    public string Language { get; init; } = "en";

    /// <summary>Fallback FFmpeg frame interval in seconds when VideoSubFinder frames are unavailable.</summary>
    [CommandOption("--ocr-interval <SECONDS>")]
    [Description("Fallback frame interval in seconds, default 2")]
    public double OcrIntervalSeconds { get; init; } = 2.0;

    /// <summary>VideoSubFinderCli executable path override; auto-discovered or downloaded when unset.</summary>
    [CommandOption("--ocr-videosubfinder-path <PATH>")]
    [Description("VideoSubFinderCli executable path override")]
    public string? OcrVideoSubFinderPath { get; init; }

    /// <summary>OCR inference backend.</summary>
    [CommandOption("--ocr-backend <BACKEND>")]
    [Description("OCR backend: zhipu, ollama, llamacpp")]
    public string OcrBackend { get; init; } = "zhipu";

    /// <summary>OCR model name.</summary>
    [CommandOption("--ocr-model <MODEL>")]
    [Description("OCR model name")]
    public string? OcrModel { get; init; }

    /// <summary>GLM-OCR API key for the zhipu backend.</summary>
    [CommandOption("--ocr-api-key <KEY>")]
    [Description("GLM-OCR API key for zhipu backend")]
    public string? OcrApiKey { get; init; }

    /// <summary>Custom OCR service endpoint.</summary>
    [CommandOption("--ocr-base-url <URL>")]
    [Description("Custom OCR endpoint")]
    public string? OcrBaseUrl { get; init; }

    /// <summary>Split strategy used for recognized text.</summary>
    [CommandOption("-s|--splitter <STRATEGY>")]
    [Description("Split strategy: rule, rule-passive, llm")]
    public string Splitter { get; init; } = "rule";

    /// <summary>LLM splitter granularity.</summary>
    [CommandOption("--splitter-chunk-granularity <LEVEL>")]
    [Description("Splitter granularity from 0 to 1, default 0.5")]
    public float ChunkGranularity { get; init; } = 0.5f;

    /// <summary>Target characters per subtitle line.</summary>
    [CommandOption("--splitter-target-length <CHARS>")]
    [Description("Target characters per subtitle line, default 50")]
    public int TargetLength { get; init; } = 50;

    /// <summary>Maximum characters per subtitle line.</summary>
    [CommandOption("--splitter-max-length <CHARS>")]
    [Description("Maximum characters per subtitle line, default 80")]
    public int MaxLength { get; init; } = 80;

    /// <summary>Line length spread range.</summary>
    [CommandOption("--splitter-spread <RANGE>")]
    [Description("Line length spread range, default 10")]
    public int SpreadRange { get; init; } = 10;

    /// <summary>Model used for LLM-based splitting.</summary>
    [CommandOption("--splitter-model <MODEL>")]
    [Description("Model for LLM-based splitting")]
    public string? SplitterModel { get; init; }

    /// <summary>API key for the LLM splitter.</summary>
    [CommandOption("--splitter-api-key <KEY>")]
    [Description("API key for LLM splitter")]
    public string? SplitterApiKey { get; init; }

    /// <summary>LLM provider used by the splitter.</summary>
    [CommandOption("--llm-provider <PROVIDER>")]
    [Description("LLM provider used by the splitter")]
    public string? LlmProvider { get; init; }

    /// <summary>Custom endpoint for the LLM splitter service.</summary>
    [CommandOption("--llm-base-url <URL>")]
    [Description("Custom LLM splitter endpoint")]
    public string? LlmBaseUrl { get; init; }
}