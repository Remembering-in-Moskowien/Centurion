using System.Text.Json.Serialization;

namespace Centurion.Models.Workflow;

/// <summary>
/// Translation QA metrics, produced by TranslationOperator and persisted to .quality.json.
/// </summary>
public class TranslationQa
{
    /// <summary>Glossary hit rate from 0 to 1: translated sentences containing the target term divided by sentences expected to hit the term; 1 when no glossary is present.</summary>
    public double GlossaryHitRate { get; set; } = 1;

    /// <summary>Number of translated sentences containing the target term.</summary>
    public int GlossaryHits { get; set; }

    /// <summary>Number of source sentences that contain a term and need the translation to hit it.</summary>
    public int GlossaryExpected { get; set; }

    /// <summary>Mean translation-to-source length ratio: &gt;1 means longer, &lt;1 means shorter, counting non-whitespace characters.</summary>
    public double MeanLengthRatio { get; set; } = 1;

    /// <summary>Mean absolute deviation of the length ratio from 1.0.</summary>
    public double LengthDeviation { get; set; }

    /// <summary>Number of sentences served from the per-sentence cache when --translation-cache is enabled.</summary>
    public int CachedSentenceCount { get; set; }

    /// <summary>Number of translated sentences.</summary>
    public int TranslatedCount { get; set; }
}
