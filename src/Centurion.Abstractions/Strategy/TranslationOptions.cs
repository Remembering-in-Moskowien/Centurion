namespace Centurion.Abstractions.Strategy;

/// <summary>
/// 翻译策略的配置选项：目标语言、术语表、目标语言台本与批处理参数。
/// </summary>
public class TranslationOptions
{
    /// <summary>源语言代码（如 "en"、"zh"、"ja"）；"auto" 表示由策略自动判断。</summary>
    public string SourceLanguage { get; init; } = "auto";

    /// <summary>目标语言代码（如 "zh"、"en"、"ja"），必填。</summary>
    public string TargetLanguage { get; init; } = "zh";

    /// <summary>术语表映射（源语言术语 → 目标语言术语），翻译时必须遵守。</summary>
    public IReadOnlyDictionary<string, string> Glossary { get; init; } = new Dictionary<string, string>();

    /// <summary>目标语言台本行（每行一句），数量与源句一致时按行号 1:1 对齐采用。</summary>
    public IReadOnlyList<string> TargetScriptLines { get; init; } = [];

    /// <summary>单次 LLM 请求翻译的句数上限。</summary>
    public int BatchSize { get; init; } = 10;

    /// <summary>批并行度：同时进行中的 LLM 翻译批数上限（默认 4；内存/限流受限时可调 1 恢复串行）。</summary>
    public int MaxConcurrency { get; init; } = 4;
}
