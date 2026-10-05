using Centurion.Models.Ass;

namespace Centurion.Core.Operators.Subtitles.Response;

/// <summary>
/// Result returned by the subtitle conversion operator.
/// </summary>
public class SubtitleConvertResponse
{
    /// <summary>The generated ASS subtitle document object.</summary>
    public required AssSub Document { get; init; }
}
