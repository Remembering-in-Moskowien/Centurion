namespace Centurion.Core.Workflow.Strategy.Translation;

/// <summary>
/// OPUS-MT model-name helpers: resolves a language-pair model key ("zh-en", "en-zh", ...)
/// from CLI settings, and maps ISO-ish language codes to the suffixes used by the
/// Helsinki-NLP OPUS-MT repo names (e.g. "ja" -> "jap", "zh-cn" -> "zh").
/// </summary>
internal static class OpusMtModelNames
{
    /// <summary>Language code -> suffix used inside the OPUS-MT repo name ("opus-mt-{src}-{tgt}").</summary>
    private static readonly Dictionary<string, string> LanguageToOpusCode = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = "en", ["eng"] = "en",
        ["zh"] = "zh", ["zho"] = "zh", ["zh-cn"] = "zh", ["zh-tw"] = "zh", ["zh-hk"] = "zh",
        ["zh-hans"] = "zh", ["zh-hant"] = "zh", ["chs"] = "zh", ["cht"] = "zh", ["cmn"] = "zh", ["yue"] = "zh",
        ["ja"] = "jap", ["ja-jp"] = "jap", ["jp"] = "jap", ["jpn"] = "jap",
        ["fr"] = "fr", ["fra"] = "fr", ["de"] = "de", ["deu"] = "de", ["es"] = "es", ["spa"] = "es",
        ["ru"] = "ru", ["rus"] = "ru", ["it"] = "it", ["ita"] = "it"
    };

    /// <summary>
    /// Resolves the OPUS-MT pair key from the CLI model name and/or source/target languages.
    /// A model name wins when given ("opus-mt-zh-en" and "zh-en" are both accepted); otherwise
    /// the pair is derived as "{source}-{target}" when both languages are explicit.
    /// </summary>
    /// <param name="model">--model value, or null.</param>
    /// <param name="sourceLanguage">Source language code, or null/"auto".</param>
    /// <param name="targetLanguage">Target language code, or null.</param>
    /// <returns>The pair key, or null when it cannot be determined.</returns>
    public static string? ResolvePair(string? model, string? sourceLanguage, string? targetLanguage)
    {
        if (!string.IsNullOrWhiteSpace(model))
        {
            var normalized = model.Trim().ToLowerInvariant();
            if (normalized.StartsWith("opus-mt-", StringComparison.Ordinal))
                normalized = normalized["opus-mt-".Length..];
            return normalized;
        }

        if (string.IsNullOrWhiteSpace(sourceLanguage) ||
            string.Equals(sourceLanguage.Trim(), "auto", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(targetLanguage))
            return null;

        var src = ToOpusCode(sourceLanguage);
        var tgt = ToOpusCode(targetLanguage);
        return src is null || tgt is null ? null : $"{src}-{tgt}";
    }

    /// <summary>Maps a language code to the OPUS-MT suffix; returns null when unmapped.</summary>
    public static string? ToOpusCode(string language)
    {
        var key = language.Trim().ToLowerInvariant();
        return LanguageToOpusCode.TryGetValue(key, out var code) ? code : null;
    }
}
