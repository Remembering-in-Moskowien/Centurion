using System.ComponentModel;
using Spectre.Console.Cli;

namespace Centurion.Cli.Commands.Settings;

/// <summary>
/// Options for the <c>translate</c> command: translate existing subtitles to a target
/// language (text-layer alignment only; timestamps are unchanged).
/// Input and output are Centurion intermediate files.
/// </summary>
public sealed class TranslateSettings : GlobalCommandSettings
{

    /// <summary>
    /// Centurion intermediate file to translate (.centurion.json).
    /// </summary>
    [CommandArgument(0, "<CENTURION_FILE>")]
    [Description("Centurion intermediate file (.centurion.json)")]
    public required FileInfo CenturionFile { get; init; }

    /// <summary>
    /// Output intermediate file; defaults to {input}.translated.centurion.json.
    /// </summary>
    [CommandOption("-o|--output <OUTPUT_FILE>")]
    [Description("Output Centurion intermediate file (default: {input}.translated.centurion.json)")]
    public FileInfo? OutputFile { get; init; }

    /// <summary>
    /// Target language code (e.g. zh, en, ja); required.
    /// </summary>
    [CommandOption("-t|--target-language <LANG>")]
    [Description("Target language code, e.g. zh, en, ja")]
    public string TargetLanguage { get; init; } = "zh";

    /// <summary>
    /// Source language code; auto-detected by the model when omitted.
    /// </summary>
    [CommandOption("--source-language <LANG>")]
    [Description("Source language code (auto-detected when omitted)")]
    public string? SourceLanguage { get; init; }

    /// <summary>
    /// Translation strategy, currently llm.
    /// </summary>
    [CommandOption("-s|--strategy <STRATEGY>")]
    [Description("Translation strategy: llm")]
    public string Strategy { get; init; } = "llm";

    /// <summary>
    /// LLM model name; defaults to gpt-4o-mini (OpenAI) or llama3.2 (Ollama) when empty.
    /// </summary>
    [CommandOption("--model <MODEL>")]
    [Description("LLM model name (OpenAI default gpt-4o-mini, Ollama default llama3.2)")]
    public string? Model { get; init; }

    /// <summary>
    /// API key for the chosen LLM provider; falls back to local Ollama when omitted.
    /// </summary>
    [CommandOption("--api-key <KEY>")]
    [Description("API key for the chosen LLM provider; falls back to local Ollama when omitted")]
    public string? ApiKey { get; init; }

    /// <summary>
    /// LLM provider (openai/deepseek/moonshot/zhipu/openrouter/groq/siliconflow/dashscope/ark/azure/ollama).
    /// </summary>
    [CommandOption("--llm-provider <PROVIDER>")]
    [Description("LLM provider: openai, deepseek, moonshot, zhipu, openrouter, groq, ollama, ...")]
    public string? LlmProvider { get; init; }

    /// <summary>
    /// Custom LLM endpoint; uses the provider default when empty.
    /// </summary>
    [CommandOption("--llm-base-url <URL>")]
    [Description("LLM base URL (defaults to provider endpoint)")]
    public string? LlmBaseUrl { get; init; }

    /// <summary>
    /// Glossary JSON file ({source term: target term} or [[source, target]]).
    /// </summary>
    [CommandOption("--glossary <FILE>")]
    [Description("Glossary JSON file: {source: target} or [[source, target]]")]
    public FileInfo? Glossary { get; init; }

    /// <summary>
    /// Target-language script file (one line per subtitle); used with 1:1 alignment when line counts match.
    /// </summary>
    [CommandOption("--target-script <FILE>")]
    [Description("Target-language script file (one line per subtitle); 1:1 alignment when line count matches")]
    public FileInfo? TargetScript { get; init; }

    /// <summary>
    /// Whether to output bilingual subtitles (source \N translation); default target-language only.
    /// </summary>
    [CommandOption("-b|--bilingual")]
    [Description("Output bilingual subtitles (source \\N translation)")]
    public bool Bilingual { get; init; }

    /// <summary>
    /// Whether to build per-word \K timestamps for translated text (time interpolation, weighted by syllable length).
    /// </summary>
    [CommandOption("-k|--karaoke")]
    [Description("Generate karaoke \\K tags for translated words (time interpolation, weighted by syllable length)")]
    public bool Karaoke { get; init; }
}
