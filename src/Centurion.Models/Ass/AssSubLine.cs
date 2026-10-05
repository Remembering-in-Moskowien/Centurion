using Centurion.Models.Ass;

namespace Centurion.Models.Ass;

/// <summary>
/// ASS dialogue or comment subtitle line.
/// </summary>
/// <param name="isComment">Whether this is a comment (true) or dialogue (false).</param>
/// <param name="layer">Layer index.</param>
/// <param name="start">Start time in milliseconds.</param>
/// <param name="end">End time in milliseconds.</param>
/// <param name="style">Style name.</param>
/// <param name="name">Speaker name.</param>
/// <param name="marginL">Left margin.</param>
/// <param name="marginR">Right margin.</param>
/// <param name="marginV">Vertical margin.</param>
/// <param name="effect">Effect tag.</param>
/// <param name="text">Subtitle text.</param>
public class AssSubLine(
    bool isComment,
    int layer,
    long start,
    long end,
    string style,
    string name,
    int marginL,
    int marginR,
    int marginV,
    string effect,
    string text)
{
    /// <summary>Whether this is a comment line.</summary>
    private readonly bool _isComment = isComment;

    /// <summary>Layer index.</summary>
    private readonly int _layer = layer;

    /// <summary>Start time in milliseconds.</summary>
    private readonly long _start = start;

    /// <summary>End time in milliseconds.</summary>
    private readonly long _end = end;

    /// <summary>Bound style name.</summary>
    private readonly string _style = string.IsNullOrWhiteSpace(style) ? string.Empty : style;

    /// <summary>Style name bound to this line and mapped to Sentence.Style by the converter.</summary>
    public string Style => _style;

    /// <summary>Subtitle text for this line, read by the converter.</summary>
    public string Text => _text;

    /// <summary>Character or speaker name.</summary>
    private readonly string _name = string.IsNullOrWhiteSpace(name) ? string.Empty : name;

    /// <summary>Left margin.</summary>
    private readonly int _marginL = marginL;

    /// <summary>Right margin.</summary>
    private readonly int _marginR = marginR;

    /// <summary>Vertical margin.</summary>
    private readonly int _marginV = marginV;

    /// <summary>ASS effect string.</summary>
    private readonly string _effect = string.IsNullOrWhiteSpace(effect) ? string.Empty : effect;

    /// <summary>Subtitle text.</summary>
    private readonly string _text = string.IsNullOrWhiteSpace(text) ? string.Empty : text;

    /// <summary>Gets the subtitle line start time in milliseconds.</summary>
    public long GetStart()
    {
        return _start;
    }

    /// <summary>Gets the subtitle line end time in milliseconds.</summary>
    public long GetEnd()
    {
        return _end;
    }

    /// <summary>
    /// Returns this line as standard ASS text.
    /// </summary>
    public override string ToString()
    {
        if (_isComment)
            return
                $"Comment: {_layer},{SubTools.LongToTime(_start)},{SubTools.LongToTime(_end)},{_style},{_name},{_marginL},{_marginR},{_marginV},{_effect},{_text}";
        return
            $"Dialogue: {_layer},{SubTools.LongToTime(_start)},{SubTools.LongToTime(_end)},{_style},{_name},{_marginL},{_marginR},{_marginV},{_effect},{_text}";
    }
}
