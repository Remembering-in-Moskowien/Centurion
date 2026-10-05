namespace Centurion.Abstractions.Strategy;

/// <summary>
/// Translation strategy options, including target language, glossary, target-language script, and batching settings.
/// </summary>
public class TranslationOptions
{
    /// <summary>Source language code, such as "en", "zh", or "ja"; "auto" lets the strategy detect it.</summary>
    public string SourceLanguage { get; init; } = "auto";

    /// <summary>Required target language code, such as "zh", "en", or "ja".</summary>
    public string TargetLanguage { get; init; } = "zh";

    /// <summary>Glossary mapping source terms to target terms; translations must follow it.</summary>
    public IReadOnlyDictionary<string, string> Glossary { get; init; } = new Dictionary<string, string>();

    /// <summary>Target-language script, one sentence per line; used for 1:1 alignment when its line count matches the source.</summary>
    public IReadOnlyList<string> TargetScriptLines { get; init; } = [];

    /// <summary>Maximum number of sentences translated in one LLM request.</summary>
    public int BatchSize { get; init; } = 10;

    /// <summary>Maximum number of concurrent LLM translation batches (default 4; set to 1 to run sequentially when memory or rate limits are constrained).</summary>
    public int MaxConcurrency { get; init; } = 4;
}
