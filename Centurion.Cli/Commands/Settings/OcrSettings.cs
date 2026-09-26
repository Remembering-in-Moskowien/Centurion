using System.ComponentModel;
using Centurion.Abstractions;
using Spectre.Console.Cli;

namespace Centurion.Cli.Commands.Settings;

/// <summary><c>ocr</c> 命令的 OCR 后端、抽帧和输出设置。</summary>
public sealed class OcrSettings : CommandSettings
{
    /// <summary>输入视频或图片文件。</summary>
    [CommandArgument(0, "<INPUT_FILE>")]
    [Description("Input video or image file")]
    public required FileInfo InputFile { get; init; }

    /// <summary>中间文件输出路径；省略时使用输入文件名加 .ocr.centurion.json。</summary>
    [CommandOption("-o|--output <OUTPUT_FILE>")]
    [Description("Output IR intermediate file")]
    public FileInfo? OutputFile { get; init; }

    /// <summary>字幕语言代码。</summary>
    [CommandOption("-l|--language <LANG>")]
    [Description("Subtitle language code, default en")]
    public string Language { get; init; } = "en";

    /// <summary>未检测到 VideoSubFinder 帧时使用的 FFmpeg 抽帧间隔。</summary>
    [CommandOption("--ocr-interval <SECONDS>")]
    [Description("Fallback frame interval in seconds, default 2")]
    public double OcrIntervalSeconds { get; init; } = 2.0;

    /// <summary>VideoSubFinder CLI 路径覆盖；未设置时自动查找或下载。</summary>
    [CommandOption("--ocr-videosubfinder-path <PATH>")]
    [Description("VideoSubFinderCli executable path override")]
    public string? OcrVideoSubFinderPath { get; init; }

    /// <summary>OCR 推理后端。</summary>
    [CommandOption("--ocr-backend <BACKEND>")]
    [Description("OCR backend: zhipu, ollama, llamacpp")]
    public string OcrBackend { get; init; } = "zhipu";

    /// <summary>OCR 模型名称。</summary>
    [CommandOption("--ocr-model <MODEL>")]
    [Description("OCR model name")]
    public string? OcrModel { get; init; }

    /// <summary>智谱 GLM-OCR API 密钥。</summary>
    [CommandOption("--ocr-api-key <KEY>")]
    [Description("GLM-OCR API key for zhipu backend")]
    public string? OcrApiKey { get; init; }

    /// <summary>自定义 OCR 服务端点。</summary>
    [CommandOption("--ocr-base-url <URL>")]
    [Description("Custom OCR endpoint")]
    public string? OcrBaseUrl { get; init; }

    /// <summary>识别文本使用的分句策略。</summary>
    [CommandOption("-s|--splitter <STRATEGY>")]
    [Description("Split strategy: rule, rule-passive, llm")]
    public string Splitter { get; init; } = "rule";

    /// <summary>LLM 分句粒度。</summary>
    [CommandOption("--splitter-chunk-granularity <LEVEL>")]
    [Description("Splitter granularity from 0 to 1, default 0.5")]
    public float ChunkGranularity { get; init; } = 0.5f;

    /// <summary>单行目标字符数。</summary>
    [CommandOption("--splitter-target-length <CHARS>")]
    [Description("Target characters per subtitle line, default 50")]
    public int TargetLength { get; init; } = 50;

    /// <summary>单行最大字符数。</summary>
    [CommandOption("--splitter-max-length <CHARS>")]
    [Description("Maximum characters per subtitle line, default 80")]
    public int MaxLength { get; init; } = 80;

    /// <summary>行长度分布扩散范围。</summary>
    [CommandOption("--splitter-spread <RANGE>")]
    [Description("Line length spread range, default 10")]
    public int SpreadRange { get; init; } = 10;

    /// <summary>LLM 分句模型名称。</summary>
    [CommandOption("--splitter-model <MODEL>")]
    [Description("Model for LLM-based splitting")]
    public string? SplitterModel { get; init; }

    /// <summary>LLM 分句 API 密钥。</summary>
    [CommandOption("--splitter-api-key <KEY>")]
    [Description("API key for LLM splitter")]
    public string? SplitterApiKey { get; init; }

    /// <summary>LLM 服务提供商。</summary>
    [CommandOption("--llm-provider <PROVIDER>")]
    [Description("LLM provider used by the splitter")]
    public string? LlmProvider { get; init; }

    /// <summary>LLM 分句服务自定义端点。</summary>
    [CommandOption("--llm-base-url <URL>")]
    [Description("Custom LLM splitter endpoint")]
    public string? LlmBaseUrl { get; init; }
}