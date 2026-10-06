namespace Centurion.Models.Workflow;

/// <summary>The correction strategy used when correcting recognition results against subtitles/scripts.</summary>
public enum CorrectionStrategy
{
    /// <summary>Correct only the timeline without rewriting the text.</summary>
    TimelineOnly,
    /// <summary>Correct only the text without adjusting the timeline.</summary>
    TextOnly,
    /// <summary>Correct both the timeline and the text.</summary>
    Both
}

/// <summary>Statistics report for a single correction (alignment) operation.</summary>
public sealed class CorrectionReport
{
    /// <summary>Total number of sentences participating in the correction.</summary>
    public int TotalSentences { get; set; }
    /// <summary>Number of sentences whose text was rewritten.</summary>
    public int TextCorrected { get; set; }
    /// <summary>Number of sentences whose timeline was shifted.</summary>
    public int TimelineShifted { get; set; }
    /// <summary>Number of sentences that could not be aligned with the script.</summary>
    public int Unmatched { get; set; }
    /// <summary>Average alignment residual drift in milliseconds.</summary>
    public double AverageDriftMs { get; set; }

    /// <summary>Maximum single-sentence drift observed during calibration in milliseconds.</summary>
    public double MaxDriftMs { get; set; }
    /// <summary>Fraction of the script text covered, from 0 to 1.</summary>
    public double TextCoverage { get; set; }
    /// <summary>Elapsed time of this correction run.</summary>
    public TimeSpan Elapsed { get; set; }
}
