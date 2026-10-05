namespace Centurion.Abstractions.Strategy;

/// <summary>
/// Options for sentence-splitting strategies.
/// </summary>
public class SplitOptions
{
    /// <summary>
    /// Maximum characters per line.
    /// </summary>
    public int MaxLength { get; set; } = 80; // Character limit.

    /// <summary>
    /// Target number of characters per line.
    /// </summary>
    public int TargetLength { get; set; } = 50; // Target character count.

    /// <summary>
    /// Maximum sentence duration in seconds.
    /// </summary>
    public double MaxDuration { get; set; } = 8.0; // Maximum duration in seconds.

    /// <summary>
    /// Minimum sentence duration in seconds; shorter sentences may be merged with adjacent sentences.
    /// </summary>
    public double MinDuration { get; set; } = 0.8; // Minimum duration in seconds.

    /// <summary>
    /// Maximum number of words per line.
    /// </summary>
    public int MaxWordsPerLine { get; set; } = 12; // Word limit.

    /// <summary>
    /// Maximum gap in seconds when merging adjacent short sentences.
    /// </summary>
    public double MergeGap { get; set; } = 1.5; // Gap allowed when merging short sentences.

    /// <summary>
    /// Whether to rewrite punctuation.
    /// </summary>
    public bool EnablePunctuationRewrite { get; set; } = true;

    /// <summary>
    /// Language code used for sentence splitting, such as "en" or "zh".
    /// </summary>
    public string Language { get; set; } = "en";

    /// <summary>
    /// Local cache directory for sentence-splitting models.
    /// </summary>
    public string ModelCachePath { get; set; } = string.Empty;

    /// <summary>
    /// Spread range for the line-length distribution.
    /// </summary>
    public int SpreadRange { get; set; }

    /// <summary>
    /// NLP chunk granularity (0.0–1.0); higher values produce finer splits.
    /// </summary>
    public float ChunkGranularity { get; set; } = 0.5f;

    /// <summary>
    /// Whether to enable a second segmentation pass.
    /// </summary>
    public bool EnableResegmentation { get; set; } = false;
}
