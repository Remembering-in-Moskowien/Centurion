namespace Centurion.Models.Workflow;

public enum CorrectionStrategy
{
    TimelineOnly,
    TextOnly,
    Both
}

public sealed class CorrectionReport
{
    public int TotalSentences { get; set; }
    public int TextCorrected { get; set; }
    public int TimelineShifted { get; set; }
    public int Unmatched { get; set; }
    public double AverageDriftMs { get; set; }
    public double TextCoverage { get; set; }
    public TimeSpan Elapsed { get; set; }
}
