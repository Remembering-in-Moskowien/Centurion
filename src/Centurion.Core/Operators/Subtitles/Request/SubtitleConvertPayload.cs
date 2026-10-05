namespace Centurion.Core.Operators.Subtitles.Request;

/// <summary>
/// Request payload for the subtitle conversion operator.
/// </summary>
public class SubtitleConvertRequest
{
    /// <summary>Path to the subtitle file.</summary>
    public required string FilePath { get; init; }

    /// <summary>Optional: explicitly specify the format (the file extension is auto-detected; an explicit format takes precedence).</summary>
    public string? Format { get; init; }
}