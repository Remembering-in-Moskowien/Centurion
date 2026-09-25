using System.ComponentModel;
using Spectre.Console.Cli;

namespace Centurion.Cli.Commands.Settings;

/// <summary>
/// <c>translate</c> 命令的选项：把已有字幕翻译到目标语言，只做文本层翻译对齐（时间轴保持不变）。
/// 输入/输出均为 Centurion 中间文件。
/// </summary>
public sealed class TranslateSettings : CommandSettings
{

    /// <summary>
    /// 待翻译的 Centurion 中间文件（.centurion.json）。
    /// </summary>
    [CommandArgument(0, "<CENTURION_FILE>")]
    [Description("Centurion intermediate file (.centurion.json)")]
    public required FileInfo CenturionFile { get; init; }

    /// <summary>
    /// 输出中间文件路径；省略时以输入名加 .translated.centurion.json 输出。
    /// </summary>
    [CommandOption("-o|--output <OUTPUT_FILE>")]
    [Description("Output Centurion intermediate file (default: <input>.translated.centurion.json)")]
    public FileInfo? OutputFile { get; init; }

    /// <summary>
    /// 目标语言代码（如 zh、en、ja），必填。
    /// </summary>
    [CommandOption("-t|--target-language <LANG>")]
    [Description("Target language code, e.g. zh, en, ja")]
    public string TargetLanguage { get; init; } = "zh";

    /// <summary>
    /// 源语言代码；为空时由模型自动判断。
    /// </summary>
    [CommandOption("--source-language <LANG>")]
    [Description("Source language code (auto-detected when omitted)")]
    public string? SourceLanguage { get; init; }

    /// <summary>
    /// 翻译策略名称，目前支持 llm。
    /// </summary>
    [CommandOption("-s|--strategy <STRATEGY>")]
    [Description("Translation strategy: llm")]
    public string Strategy { get; init; } = "llm";

    /// <summary>
    /// LLM 模型名称；为空时 OpenAI 默认 gpt-4o-mini、Ollama 默认 llama3.1。
    /// </summary>
    [CommandOption("--model <MODEL>")]
    [Description("LLM model name (OpenAI default gpt-4o-mini, Ollama default llama3.1)")]
    public string? Model { get; init; }

    /// <summary>
    /// OpenAI API 密钥；提供时使用 OpenAI 后端，否则回退本地 Ollama。
    /// </summary>
    [CommandOption("--api-key <KEY>")]
    [Description("API key for the chosen LLM provider; falls back to local Ollama when omitted")]
    public string? ApiKey { get; init; }

    /// <summary>
    /// LLM 服务提供商名（openai/deepseek/moonshot/zhipu/openrouter/groq/siliconflow/dashscope/ark/azure/ollama）。
    /// </summary>
    [CommandOption("--llm-provider <PROVIDER>")]
    [Description("LLM provider: openai, deepseek, moonshot, zhipu, openrouter, groq, ollama, ...")]
    public string? LlmProvider { get; init; }

    /// <summary>
    /// LLM 自定义端点；为空时使用所选提供商默认端点。
    /// </summary>
    [CommandOption("--llm-base-url <URL>")]
    [Description("LLM base URL (defaults to provider endpoint)")]
    public string? LlmBaseUrl { get; init; }

    /// <summary>
    /// 术语表 JSON 文件路径（{源语言术语: 目标语言术语} 或 [{source,target}]）。
    /// </summary>
    [CommandOption("--glossary <FILE>")]
    [Description("Glossary JSON file: {source: target} or [[source, target]]")]
    public FileInfo? Glossary { get; init; }

    /// <summary>
    /// 目标语言台本文件路径（每行一句）；行数与字幕一致时按行号 1:1 对齐采用。
    /// </summary>
    [CommandOption("--target-script <FILE>")]
    [Description("Target-language script file (one line per subtitle); 1:1 alignment when line count matches")]
    public FileInfo? TargetScript { get; init; }

    /// <summary>
    /// 是否输出双语字幕（原文 \N 译文）；默认仅目标语言。
    /// </summary>
    [CommandOption("-b|--bilingual")]
    [Description("Output bilingual subtitles (source \\N translation)")]
    public bool Bilingual { get; init; }

    /// <summary>
    /// 是否输出卡拉OK模式：为译文构建词级 \K 时间戳（时间插值 + 长音节词多分配）。
    /// </summary>
    [CommandOption("-k|--karaoke")]
    [Description("Generate karaoke \\K tags for translated words (time interpolation, weighted by syllable length)")]
    public bool Karaoke { get; init; }
}
