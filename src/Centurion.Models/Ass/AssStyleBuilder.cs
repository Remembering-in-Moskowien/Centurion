using System.Text.RegularExpressions;

namespace Centurion.Models.Ass;

/// <summary>
/// Fluent builder for quickly creating ASS Style definitions.
/// </summary>
public partial class AssStyleBuilder : BuilderBase<AssStyleBuilder, AssStyle>
{
    private string _name = string.Empty;
    private string _fontName = string.Empty;
    private int _fontSize;
    private string _primaryColour = string.Empty;
    private string _secondaryColour = string.Empty;
    private string _outlineColour = string.Empty;
    private string _backColour = string.Empty;
    private bool _bold;
    private bool _italic;
    private bool _underline;
    private bool _strikeOut;
    private float _scaleX;
    private float _scaleY;
    private float _spacing;
    private float _angle;
    private int _borderStyle;
    private float _outline;
    private float _shadow;
    private int _alignment;
    private int _marginL;
    private int _marginR;
    private int _marginV;
    private int _encoding;

    /// <summary>Style name used to reference the style in the document.</summary>
    public string Name => _name;
    /// <summary>Font name.</summary>
    public string FontName => _fontName;
    /// <summary>Font size in pixels.</summary>
    public int FontSize => _fontSize;
    /// <summary>Primary text color in ASS format, such as &amp;H00FFFFFF.</summary>
    public string PrimaryColour => _primaryColour;
    /// <summary>Secondary fill color used for unsung karaoke text.</summary>
    public string SecondaryColour => _secondaryColour;
    /// <summary>Outline color.</summary>
    public string OutlineColour => _outlineColour;
    /// <summary>Shadow/background color.</summary>
    public string BackColour => _backColour;
    /// <summary>Whether the text is bold.</summary>
    public bool Bold => _bold;
    /// <summary>Whether the text is italic.</summary>
    public bool Italic => _italic;
    /// <summary>Whether the text is underlined.</summary>
    public bool Underline => _underline;
    /// <summary>Whether the text is struck through.</summary>
    public bool StrikeOut => _strikeOut;
    /// <summary>Horizontal scale percentage.</summary>
    public float ScaleX => _scaleX;
    /// <summary>Vertical scale percentage.</summary>
    public float ScaleY => _scaleY;
    /// <summary>Character spacing in pixels.</summary>
    public float Spacing => _spacing;
    /// <summary>Text rotation angle in degrees; counterclockwise is positive.</summary>
    public float Angle => _angle;
    /// <summary>Outline rendering mode (1 = outline with opaque fill, 3 = opaque box).</summary>
    public int BorderStyle => _borderStyle;
    /// <summary>Outline width.</summary>
    public float Outline => _outline;
    /// <summary>Shadow depth.</summary>
    public float Shadow => _shadow;
    /// <summary>Alignment using ASS values 1–9.</summary>
    public int Alignment => _alignment;
    /// <summary>Left safe-area margin.</summary>
    public int MarginL => _marginL;
    /// <summary>Right safe-area margin.</summary>
    public int MarginR => _marginR;
    /// <summary>Vertical safe-area margin.</summary>
    public int MarginV => _marginV;
    /// <summary>Text encoding ID, such as 1 for the system default.</summary>
    public int Encoding => _encoding;

    /// <summary>Sets the style name.</summary>
    public AssStyleBuilder WithName(string value)
    {
        return Set(ref _name, string.IsNullOrWhiteSpace(value) ? string.Empty : value);
    }

    /// <summary>Sets the font name.</summary>
    public AssStyleBuilder WithFontName(string value)
    {
        return Set(ref _fontName, string.IsNullOrWhiteSpace(value) ? string.Empty : value);
    }

    /// <summary>Sets the font size.</summary>
    public AssStyleBuilder WithFontSize(int value)
    {
        return Set(ref _fontSize, value);
    }

    /// <summary>Sets the primary text color.</summary>
    public AssStyleBuilder WithPrimaryColour(string value)
    {
        return Set(ref _primaryColour, string.IsNullOrWhiteSpace(value) ? string.Empty : value);
    }

    /// <summary>Sets the secondary fill color.</summary>
    public AssStyleBuilder WithSecondaryColour(string value)
    {
        return Set(ref _secondaryColour, string.IsNullOrWhiteSpace(value) ? string.Empty : value);
    }

    /// <summary>Sets the outline color.</summary>
    public AssStyleBuilder WithOutlineColour(string value)
    {
        return Set(ref _outlineColour, string.IsNullOrWhiteSpace(value) ? string.Empty : value);
    }

    /// <summary>Sets the shadow color.</summary>
    public AssStyleBuilder WithBackColour(string value)
    {
        return Set(ref _backColour, string.IsNullOrWhiteSpace(value) ? string.Empty : value);
    }

    /// <summary>Enables or disables bold text.</summary>
    public AssStyleBuilder WithBold(bool value)
    {
        return Set(ref _bold, value);
    }

    /// <summary>Enables or disables italic text.</summary>
    public AssStyleBuilder WithItalic(bool value)
    {
        return Set(ref _italic, value);
    }

    /// <summary>Enables or disables underlining.</summary>
    public AssStyleBuilder WithUnderline(bool value)
    {
        return Set(ref _underline, value);
    }

    /// <summary>Enables or disables strikethrough.</summary>
    public AssStyleBuilder WithStrikeOut(bool value)
    {
        return Set(ref _strikeOut, value);
    }

    /// <summary>Sets the horizontal scale.</summary>
    public AssStyleBuilder WithScaleX(float value)
    {
        return Set(ref _scaleX, value);
    }

    /// <summary>Sets the vertical scale.</summary>
    public AssStyleBuilder WithScaleY(float value)
    {
        return Set(ref _scaleY, value);
    }

    /// <summary>Sets the character spacing.</summary>
    public AssStyleBuilder WithSpacing(float value)
    {
        return Set(ref _spacing, value);
    }

    /// <summary>Sets the text rotation angle.</summary>
    public AssStyleBuilder WithAngle(float value)
    {
        return Set(ref _angle, value);
    }

    /// <summary>Sets the outline rendering mode.</summary>
    public AssStyleBuilder WithBorderStyle(int value)
    {
        return Set(ref _borderStyle, value);
    }

    /// <summary>Sets the outline width.</summary>
    public AssStyleBuilder WithOutline(float value)
    {
        return Set(ref _outline, value);
    }

    /// <summary>Sets the shadow depth.</summary>
    public AssStyleBuilder WithShadow(float value)
    {
        return Set(ref _shadow, value);
    }

    /// <summary>Sets subtitle alignment.</summary>
    public AssStyleBuilder WithAlignment(int value)
    {
        return Set(ref _alignment, value);
    }

    /// <summary>Sets the overall left margin.</summary>
    public AssStyleBuilder WithMarginL(int value)
    {
        return Set(ref _marginL, value);
    }

    /// <summary>Sets the overall right margin.</summary>
    public AssStyleBuilder WithMarginR(int value)
    {
        return Set(ref _marginR, value);
    }

    /// <summary>Sets the overall vertical margin.</summary>
    public AssStyleBuilder WithMarginV(int value)
    {
        return Set(ref _marginV, value);
    }

    /// <summary>Sets the text encoding ID.</summary>
    public AssStyleBuilder WithEncoding(int value)
    {
        return Set(ref _encoding, value);
    }

    /// <summary>
    /// Populates the default primary subtitle style (sans-serif, modeled after the English video style in Theme.ass):
    /// uses bold Arial (available on Windows/macOS; metrically compatible Liberation Sans is used on Linux),
    /// with a semi-transparent outline and shadow, bottom-centered with a vertical margin of 100.
    /// </summary>
    public AssStyleBuilder WithDefaultValues()
    {
        return WithName("Default")
            .WithFontName("Arial")
            .WithFontSize(84)
            .WithPrimaryColour("&H00FFFFFF")
            .WithSecondaryColour("&H00FFFFFF")
            .WithOutlineColour("&H37000000")
            .WithBackColour("&H370E0807")
            .WithBold(true)
            .WithItalic(false)
            .WithUnderline(false)
            .WithStrikeOut(false)
            .WithScaleX(100.0f)
            .WithScaleY(100.0f)
            .WithSpacing(0.0f)
            .WithAngle(0.0f)
            .WithBorderStyle(1)
            .WithOutline(3.3f)
            .WithShadow(2.5f)
            .WithAlignment(2)
            .WithMarginL(9)
            .WithMarginR(9)
            .WithMarginV(100)
            .WithEncoding(1);
    }

    /// <summary>
    /// Populates the secondary subtitle style (Chinese sans-serif, modeled after the target-language chi style in Theme.ass):
    /// uses Microsoft YaHei on Windows, PingFang SC on macOS, and Noto Sans CJK SC on Linux,
    /// with a bottom-aligned vertical margin of 28 for translated lines in bilingual subtitles.
    /// </summary>
    public AssStyleBuilder WithSubtitleStyle()
    {
        return WithName("Sub")
            .WithFontName("Microsoft YaHei")
            .WithFontSize(81)
            .WithPrimaryColour("&H00FFFFFF")
            .WithSecondaryColour("&H00FFFFFF")
            .WithOutlineColour("&H37000000")
            .WithBackColour("&H370E0807")
            .WithBold(true)
            .WithItalic(false)
            .WithUnderline(false)
            .WithStrikeOut(false)
            .WithScaleX(100.0f)
            .WithScaleY(100.0f)
            .WithSpacing(0.0f)
            .WithAngle(0.0f)
            .WithBorderStyle(1)
            .WithOutline(3.3f)
            .WithShadow(2.5f)
            .WithAlignment(2)
            .WithMarginL(9)
            .WithMarginR(9)
            .WithMarginV(28)
            .WithEncoding(1);
    }

    /// <summary>Parses a raw Style line into a builder.</summary>
    /// <param name="content">Raw Style line.</param>
    /// <returns>A populated builder.</returns>
    /// <exception cref="FormatException">Thrown when the format is invalid.</exception>
    public static AssStyleBuilder FromContent(string content)
    {
        var builder = new AssStyleBuilder();
        var match = StyleLineRegex().Match(content);
        if (match.Success)
            builder = builder
                .WithName(match.Groups[1].Value)
                .WithFontName(match.Groups[2].Value)
                .WithFontSize(int.Parse(match.Groups[3].Value))
                .WithPrimaryColour(match.Groups[4].Value)
                .WithSecondaryColour(match.Groups[5].Value)
                .WithOutlineColour(match.Groups[6].Value)
                .WithBackColour(match.Groups[7].Value)
                .WithBold(int.Parse(match.Groups[8].Value) == -1)
                .WithItalic(int.Parse(match.Groups[9].Value) == -1)
                .WithUnderline(int.Parse(match.Groups[10].Value) == -1)
                .WithStrikeOut(int.Parse(match.Groups[11].Value) == -1)
                .WithScaleX(float.Parse(match.Groups[12].Value))
                .WithScaleY(float.Parse(match.Groups[13].Value))
                .WithSpacing(float.Parse(match.Groups[14].Value))
                .WithAngle(float.Parse(match.Groups[15].Value))
                .WithBorderStyle(int.Parse(match.Groups[16].Value))
                .WithOutline(float.Parse(match.Groups[17].Value))
                .WithShadow(float.Parse(match.Groups[18].Value))
                .WithAlignment(int.Parse(match.Groups[19].Value))
                .WithMarginL(int.Parse(match.Groups[20].Value))
                .WithMarginR(int.Parse(match.Groups[21].Value))
                .WithMarginV(int.Parse(match.Groups[22].Value))
                .WithEncoding(int.Parse(match.Groups[23].Value));
        else
            throw new FormatException("Style line does not match the ASS format.");
        return builder;
    }

    /// <summary>Builds an AssStyle instance.</summary>
    public override AssStyle Build()
    {
        return new AssStyle(_name, _fontName, _fontSize, _primaryColour, _secondaryColour, _outlineColour, _backColour,
            _bold, _italic, _underline, _strikeOut, _scaleX, _scaleY, _spacing, _angle, _borderStyle, _outline, _shadow,
            _alignment, _marginL, _marginR, _marginV, _encoding);
    }

    /// <summary>Regular expression matching an ASS Style definition line.</summary>
    [GeneratedRegex(
        @"^Style:\s*" +
        @"([^,]+)," +
        @"([^,]+)," +
        @"(\d+)," +
        @"([^,]+)," +
        @"([^,]+)," +
        @"([^,]+)," +
        @"([^,]+)," +
        @"(-?\d+)," +
        @"(-?\d+)," +
        @"(-?\d+)," +
        @"(-?\d+)," +
        @"([\d.]+)," +
        @"([\d.]+)," +
        @"([\d.]+)," +
        @"([\d.]+)," +
        @"(\d+)," +
        @"([\d.]+)," +
        @"([\d.]+)," +
        @"(\d+)," +
        @"(\d+)," +
        @"(\d+)," +
        @"(\d+)," +
        @"(\d+)$",
        RegexOptions.Singleline
    )]
    private static partial Regex StyleLineRegex();
}
