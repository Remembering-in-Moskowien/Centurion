using Centurion.Models.Llm;

namespace Centurion.Abstractions.Factories;

/// <summary>
/// Request-scoped options for creating a translation strategy. Carries everything a strategy
/// needs to be constructed from CLI settings: LLM connection details (for the "llm" strategy),
/// the model name and language pair (for the "opus" strategy), and decoding parameters.
/// </summary>
public sealed record TranslationRequestOptions
{
    /// <summary>Source language code, such as "en", "zh" or "ja"; "auto" when unknown.</summary>
    public string? SourceLanguage { get; init; }

    /// <summary>Target language code, such as "zh", "en" or "ja".</summary>
    public string? TargetLanguage { get; init; }

    /// <summary>Model name: an LLM model for "llm", an OPUS-MT pair ("zh-en", "en-zh", ...) for "opus".</summary>
    public string? Model { get; init; }

    /// <summary>Beam size for OPUS-MT decoding (1 = greedy).</summary>
    public int BeamSize { get; init; } = 4;

    /// <summary>Maximum decoded token length for OPUS-MT.</summary>
    public int MaxLength { get; init; } = 256;

    /// <summary>LLM connection options for the "llm" strategy.</summary>
    public LlmOptions? Llm { get; init; }
}
