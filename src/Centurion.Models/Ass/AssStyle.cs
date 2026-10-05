namespace Centurion.Models.Ass;

using Newtonsoft.Json;

/// <summary>
/// ASS subtitle style definition.
/// </summary>
/// <param name="name">Style name.</param>
/// <param name="fontName">Font name.</param>
/// <param name="fontSize">Font size.</param>
/// <param name="primaryColour">Primary text color.</param>
/// <param name="secondaryColour">Secondary fill color.</param>
/// <param name="outlineColour">Outline color.</param>
/// <param name="backColour">Shadow color.</param>
/// <param name="bold">Whether the text is bold.</param>
/// <param name="italic">Whether the text is italic.</param>
/// <param name="underline">Whether the text is underlined.</param>
/// <param name="strikeOut">Whether the text is struck through.</param>
/// <param name="scaleX">Horizontal scale.</param>
/// <param name="scaleY">Vertical scale.</param>
/// <param name="spacing">Character spacing.</param>
/// <param name="angle">Rotation angle.</param>
/// <param name="borderStyle">Border style.</param>
/// <param name="outline">Outline width.</param>
/// <param name="shadow">Shadow depth.</param>
/// <param name="alignment">Alignment (1–9).</param>
/// <param name="marginL">Left margin.</param>
/// <param name="marginR">Right margin.</param>
/// <param name="marginV">Vertical margin.</param>
/// <param name="encoding">Text encoding ID.</param>
[System.Text.Json.Serialization.JsonConverter(typeof(Centurion.Models.Ass.AssStyleJsonConverter))]
public class AssStyle(
    string name,
    string fontName,
    int fontSize,
    string primaryColour,
    string secondaryColour,
    string outlineColour,
    string backColour,
    bool bold,
    bool italic,
    bool underline,
    bool strikeOut,
    float scaleX,
    float scaleY,
    float spacing,
    float angle,
    int borderStyle,
    float outline,
    float shadow,
    int alignment,
    int marginL,
    int marginR,
    int marginV,
    int encoding)
{
    /// <summary>Unique style name.</summary>
    private readonly string _name = string.IsNullOrWhiteSpace(name) ? string.Empty : name;

    /// <summary>Font name.</summary>
    private readonly string _fontName = string.IsNullOrWhiteSpace(fontName) ? string.Empty : fontName;

    /// <summary>Font size.</summary>
    private readonly int _fontSize = fontSize;

    /// <summary>Primary text color.</summary>
    private readonly string _primaryColour = string.IsNullOrWhiteSpace(primaryColour) ? string.Empty : primaryColour;

    /// <summary>Secondary fill color.</summary>
    private readonly string _secondaryColour =
        string.IsNullOrWhiteSpace(secondaryColour) ? string.Empty : secondaryColour;

    /// <summary>Text outline color.</summary>
    private readonly string _outlineColour = string.IsNullOrWhiteSpace(outlineColour) ? string.Empty : outlineColour;

    /// <summary>Shadow fill color.</summary>
    private readonly string _backColour = string.IsNullOrWhiteSpace(backColour) ? string.Empty : backColour;

    /// <summary>Bold setting.</summary>
    private readonly bool _bold = bold;

    /// <summary>Italic setting.</summary>
    private readonly bool _italic = italic;

    /// <summary>Underline setting.</summary>
    private readonly bool _underline = underline;

    /// <summary>Strikethrough setting.</summary>
    private readonly bool _strikeOut = strikeOut;

    /// <summary>Horizontal scale.</summary>
    private readonly float _scaleX = scaleX;

    /// <summary>Vertical scale.</summary>
    private readonly float _scaleY = scaleY;

    /// <summary>Character spacing.</summary>
    private readonly float _spacing = spacing;

    /// <summary>Text rotation angle.</summary>
    private readonly float _angle = angle;

    /// <summary>Outline rendering mode.</summary>
    private readonly int _borderStyle = borderStyle;

    /// <summary>Outline width.</summary>
    private readonly float _outline = outline;

    /// <summary>Shadow width.</summary>
    private readonly float _shadow = shadow;

    /// <summary>Subtitle alignment.</summary>
    private readonly int _alignment = alignment;

    /// <summary>Overall left margin.</summary>
    private readonly int _marginL = marginL;

    /// <summary>Overall right margin.</summary>
    private readonly int _marginR = marginR;

    /// <summary>Overall vertical margin.</summary>
    private readonly int _marginV = marginV;

    /// <summary>Text encoding identifier.</summary>
    private readonly int _encoding = encoding;

    // Public read-only properties for Newtonsoft serialization/deserialization and rendering.
    // JSON field names match the Studio frontend IAssStyle contract (lowercase forms such as fontname and fontsize).
    /// <summary>Style name.</summary>
    [JsonProperty("name")] public string Name => _name;
    /// <summary>Font name.</summary>
    [JsonProperty("fontname")] public string FontName => _fontName;
    /// <summary>Font size in pixels.</summary>
    [JsonProperty("fontsize")] public int FontSize => _fontSize;
    /// <summary>Primary text color in ASS format, such as &amp;H00FFFFFF.</summary>
    [JsonProperty("primaryColour")] public string PrimaryColour => _primaryColour;
    /// <summary>Secondary fill color used for unsung karaoke text.</summary>
    [JsonProperty("secondaryColour")] public string SecondaryColour => _secondaryColour;
    /// <summary>Outline color.</summary>
    [JsonProperty("outlineColour")] public string OutlineColour => _outlineColour;
    /// <summary>Shadow/background color.</summary>
    [JsonProperty("backColour")] public string BackColour => _backColour;
    /// <summary>Whether the text is bold.</summary>
    [JsonProperty("bold")] public bool Bold => _bold;
    /// <summary>Whether the text is italic.</summary>
    [JsonProperty("italic")] public bool Italic => _italic;
    /// <summary>Whether the text is underlined.</summary>
    [JsonProperty("underline")] public bool Underline => _underline;
    /// <summary>Whether the text is struck through.</summary>
    [JsonProperty("strikeOut")] public bool StrikeOut => _strikeOut;
    /// <summary>Horizontal scale percentage.</summary>
    [JsonProperty("scaleX")] public float ScaleX => _scaleX;
    /// <summary>Vertical scale percentage.</summary>
    [JsonProperty("scaleY")] public float ScaleY => _scaleY;
    /// <summary>Character spacing in pixels.</summary>
    [JsonProperty("spacing")] public float Spacing => _spacing;
    /// <summary>Text rotation angle in degrees; counterclockwise is positive.</summary>
    [JsonProperty("angle")] public float Angle => _angle;
    /// <summary>Outline rendering mode (1 = outline with opaque fill, 3 = opaque box).</summary>
    [JsonProperty("borderStyle")] public int BorderStyle => _borderStyle;
    /// <summary>Outline width.</summary>
    [JsonProperty("outline")] public float Outline => _outline;
    /// <summary>Shadow depth.</summary>
    [JsonProperty("shadow")] public float Shadow => _shadow;
    /// <summary>Alignment using ASS values 1–9.</summary>
    [JsonProperty("alignment")] public int Alignment => _alignment;
    /// <summary>Left safe-area margin.</summary>
    [JsonProperty("marginL")] public int MarginL => _marginL;
    /// <summary>Right safe-area margin.</summary>
    [JsonProperty("marginR")] public int MarginR => _marginR;
    /// <summary>Vertical safe-area margin.</summary>
    [JsonProperty("marginV")] public int MarginV => _marginV;
    /// <summary>Text encoding ID.</summary>
    [JsonProperty("encoding")] public int Encoding => _encoding;

    /// <summary>Formats this style as a standard ASS Style line.</summary>
    public override string ToString()
    {
        return
            $"Style: {_name},{_fontName},{_fontSize},{_primaryColour},{_secondaryColour},{_outlineColour},{_backColour},{(_bold ? -1 : 0)},{(_italic ? -1 : 0)},{(_underline ? -1 : 0)},{(_strikeOut ? -1 : 0)},{_scaleX},{_scaleY},{_spacing},{_angle},{_borderStyle},{_outline},{_shadow},{_alignment},{_marginL},{_marginR},{_marginV},{_encoding}";
    }

    /// <summary>Compares styles by name.</summary>
    public override bool Equals(object? obj)
    {
        if (obj is AssStyle other) return _name == other._name;
        return false;
    }

    /// <summary>Creates a hash code from the style name.</summary>
    public override int GetHashCode()
    {
        return _name?.GetHashCode() ?? 0;
    }
}
