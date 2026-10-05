namespace Centurion.Core.Processing.Text;

/// <summary>
/// Key-name constants used in the extended-data dictionary for text-correction metadata.
/// </summary>
public static class CorrectKeys
{
    /// <summary>Key name for the original (pre-correction) text source.</summary>
    public const string Origin = "correct.origin";
    /// <summary>Key name for the time-drift amount (milliseconds).</summary>
    public const string DriftMs = "correct.driftMs";
    /// <summary>Key name for the correction action taken.</summary>
    public const string Action = "correct.action";
    /// <summary>Key name for the text match ratio.</summary>
    public const string MatchRatio = "correct.matchRatio";
}