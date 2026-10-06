using System.Text.RegularExpressions;
using Centurion.Models.Ass;

namespace Centurion.Models.Ass;

/// <summary>
/// Streaming builder for ASS subtitle lines; quickly creates Dialogue/Comment lines.
/// </summary>
public partial class AssSubLineBuilder : BuilderBase<AssSubLineBuilder, AssSubLine>
{
    /// <summary>Whether this is a comment line.</summary>
    private bool _isComment;

    /// <summary>Layer number.</summary>
    private int _layer;

    /// <summary>Start time in milliseconds.</summary>
    private long _start;

    /// <summary>End time in milliseconds.</summary>
    private long _end;

    /// <summary>Associated style name.</summary>
    private string _style = string.Empty;

    /// <summary>Speaker name.</summary>
    private string _name = string.Empty;

    /// <summary>Left margin.</summary>
    private int _marginL;

    /// <summary>Right margin.</summary>
    private int _marginR;

    /// <summary>Vertical margin.</summary>
    private int _marginV;

    /// <summary>Effect string.</summary>
    private string _effect = string.Empty;

    /// <summary>Subtitle text.</summary>
    private string _text = string.Empty;

    /// <summary>Whether this is a comment line.</summary>
    public bool IsComment => _isComment;

    /// <summary>Layer level.</summary>
    public int Layer => _layer;

    /// <summary>Start time (milliseconds).</summary>
    public long Start => _start;

    /// <summary>End time (milliseconds).</summary>
    public long End => _end;

    /// <summary>Style name.</summary>
    public string Style => _style;

    /// <summary>Speaker.</summary>
    public string Name => _name;

    /// <summary>Left margin.</summary>
    public int MarginL => _marginL;

    /// <summary>Right margin.</summary>
    public int MarginR => _marginR;

    /// <summary>Vertical margin.</summary>
    public int MarginV => _marginV;

    /// <summary>ASS effect.</summary>
    public string Effect => _effect;

    /// <summary>Subtitle text.</summary>
    public string Text => _text;

    /// <summary>Sets whether this is a comment line.</summary>
    public AssSubLineBuilder WithComment(bool value)
    {
        return Set(ref _isComment, value);
    }

    /// <summary>Sets the layer number.</summary>
    public AssSubLineBuilder WithLayer(int value)
    {
        return Set(ref _layer, value);
    }

    /// <summary>Sets the start time in milliseconds.</summary>
    public AssSubLineBuilder WithStart(long value)
    {
        return Set(ref _start, value);
    }

    /// <summary>Sets the end time in milliseconds.</summary>
    public AssSubLineBuilder WithEnd(long value)
    {
        return Set(ref _end, value);
    }

    /// <summary>Sets the associated style name.</summary>
    public AssSubLineBuilder WithStyle(string value)
    {
        return Set(ref _style, string.IsNullOrWhiteSpace(value) ? string.Empty : value);
    }

    /// <summary>Sets the speaker name.</summary>
    public AssSubLineBuilder WithName(string value)
    {
        return Set(ref _name, string.IsNullOrWhiteSpace(value) ? string.Empty : value);
    }

    /// <summary>Sets the left margin.</summary>
    public AssSubLineBuilder WithMarginL(int value)
    {
        return Set(ref _marginL, value);
    }

    /// <summary>Sets the right margin.</summary>
    public AssSubLineBuilder WithMarginR(int value)
    {
        return Set(ref _marginR, value);
    }

    /// <summary>Sets the vertical margin.</summary>
    public AssSubLineBuilder WithMarginV(int value)
    {
        return Set(ref _marginV, value);
    }

    /// <summary>Sets the subtitle effect.</summary>
    public AssSubLineBuilder WithEffect(string value)
    {
        return Set(ref _effect, string.IsNullOrWhiteSpace(value) ? string.Empty : value);
    }

    /// <summary>Sets the subtitle text.</summary>
    public AssSubLineBuilder WithText(string value)
    {
        return Set(ref _text, string.IsNullOrWhiteSpace(value) ? string.Empty : value);
    }

    /// <summary>
    /// Builds a builder instance parsed from a raw ASS line.
    /// </summary>
    /// <param name="content">Raw Dialogue/Comment line.</param>
    /// <returns>A fully populated builder.</returns>
    /// <exception cref="FormatException">Thrown when the text format is invalid.</exception>
    public static AssSubLineBuilder FromContent(string content)
    {
        var builder = new AssSubLineBuilder();
        var match = DialogueRegex().Match(content);
        if (match.Success)
            builder = builder
                .WithComment(match.Groups[1].Value == "Comment")
                .WithLayer(int.Parse(match.Groups[2].Value))
                .WithStart(SubTools.TimeToLong(match.Groups[3].Value))
                .WithEnd(SubTools.TimeToLong(match.Groups[4].Value))
                .WithStyle(match.Groups[5].Value)
                .WithName(match.Groups[6].Value)
                .WithMarginL(int.Parse(match.Groups[7].Value))
                .WithMarginR(int.Parse(match.Groups[8].Value))
                .WithMarginV(int.Parse(match.Groups[9].Value))
                .WithEffect(match.Groups[10].Value)
                .WithText(match.Groups[11].Value);
        else
            throw new FormatException("字幕行格式不符合ASS标准");
        return builder;
    }

    /// <summary>Builds an AssSubLine instance from the current configuration.</summary>
    public override AssSubLine Build()
    {
        return new AssSubLine(_isComment, _layer, _start, _end, _style, _name, _marginL, _marginR, _marginV, _effect,
            _text);
    }

    /// <summary>Regex matching a single ASS Dialogue/Comment line.</summary>
    [GeneratedRegex(
        @"^(Comment|Dialogue):\s*" +
        @"(\d+)," +
        @"([^,]+)," +
        @"([^,]+)," +
        @"([^,]+)," +
        @"([^,]*)," +
        @"(\d+)," +
        @"(\d+)," +
        @"(\d+)," +
        @"([^,]*)," +
        @"(.*)$",
        RegexOptions.Singleline
    )]
    private static partial Regex DialogueRegex();
}
