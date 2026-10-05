using System.Text;

namespace Centurion.Models.Ass;

/// <summary>
/// Complete ASS subtitle document model containing script information, styles, and dialogue lines.
/// </summary>
/// <param name="title">Subtitle title.</param>
/// <param name="scriptType">Script version identifier.</param>
/// <param name="wrapStyle">Automatic line-wrapping mode.</param>
/// <param name="collisions">Overlap handling mode.</param>
/// <param name="playResX">Reference video width.</param>
/// <param name="playResY">Reference video height.</param>
/// <param name="timer">Time scaling factor.</param>
/// <param name="styles">Style list.</param>
/// <param name="lines">Dialogue and comment lines.</param>
public class AssSub(
    string title,
    string scriptType,
    string wrapStyle,
    string collisions,
    string playResX,
    string playResY,
    float timer,
    List<AssStyle> styles,
    List<AssSubLine> lines)
{
    /// <summary>Subtitle document title.</summary>
    private readonly string _title = string.IsNullOrWhiteSpace(title) ? string.Empty : title;

    /// <summary>Script type/version.</summary>
    private readonly string _scriptType = string.IsNullOrWhiteSpace(scriptType) ? string.Empty : scriptType;

    /// <summary>Automatic line-wrapping mode.</summary>
    private readonly string _wrapStyle = string.IsNullOrWhiteSpace(wrapStyle) ? string.Empty : wrapStyle;

    /// <summary>Subtitle overlap handling policy.</summary>
    private readonly string _collisions = string.IsNullOrWhiteSpace(collisions) ? string.Empty : collisions;

    /// <summary>Reference resolution width.</summary>
    private readonly string _playResX = string.IsNullOrWhiteSpace(playResX) ? string.Empty : playResX;

    /// <summary>Reference resolution height.</summary>
    private readonly string _playResY = string.IsNullOrWhiteSpace(playResY) ? string.Empty : playResY;

    /// <summary>Timer scale.</summary>
    private readonly float _timer = timer;

    /// <summary>All style definitions.</summary>
    private readonly List<AssStyle> _styles = styles ?? [];

    /// <summary>All subtitle dialogue and comment lines.</summary>
    private readonly List<AssSubLine> _lines = lines ?? [];

    /// <summary>Returns the complete standard ASS file text.</summary>
    public override string ToString()
    {
        StringBuilder sb = new();
        sb.AppendLine("[Script Info]");
        sb.AppendLine($"Title: {_title}");
        sb.AppendLine($"ScriptType: {_scriptType}");
        sb.AppendLine($"WrapStyle: {_wrapStyle}");
        sb.AppendLine($"Collisions: {_collisions}");
        sb.AppendLine($"PlayResX: {_playResX}");
        sb.AppendLine($"PlayResY: {_playResY}");
        sb.AppendLine($"Timer: {_timer}");
        sb.AppendLine();

        sb.AppendLine("[V4+ Styles]");
        // Add the Style format line.
        sb.AppendLine("Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding");
        foreach (var style in _styles)
            sb.AppendLine(style.ToString());
        sb.AppendLine();

        sb.AppendLine("[Events]");
        // Add the Event format line.
        sb.AppendLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");
        foreach (var line in _lines)
            sb.AppendLine(line.ToString());

        return sb.ToString();
    }
}
