namespace Centurion.Models.Ass;

using Newtonsoft.Json;

/// <summary>
/// ASS字幕样式定义实体
/// </summary>
/// <param name="name">样式名称</param>
/// <param name="fontName">字体名称</param>
/// <param name="fontSize">字号</param>
/// <param name="primaryColour">主文字颜色</param>
/// <param name="secondaryColour">次要填充色</param>
/// <param name="outlineColour">描边颜色</param>
/// <param name="backColour">阴影颜色</param>
/// <param name="bold">是否加粗</param>
/// <param name="italic">是否斜体</param>
/// <param name="underline">是否下划线</param>
/// <param name="strikeOut">是否删除线</param>
/// <param name="scaleX">横向缩放</param>
/// <param name="scaleY">纵向缩放</param>
/// <param name="spacing">字间距</param>
/// <param name="angle">旋转角度</param>
/// <param name="borderStyle">边框类型</param>
/// <param name="outline">描边粗细</param>
/// <param name="shadow">阴影厚度</param>
/// <param name="alignment">对齐方式(1~9)</param>
/// <param name="marginL">左外边距</param>
/// <param name="marginR">右外边距</param>
/// <param name="marginV">垂直外边距</param>
/// <param name="encoding">文字编码ID</param>
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
    /// <summary>样式唯一名称</summary>
    private readonly string _name = string.IsNullOrWhiteSpace(name) ? string.Empty : name;

    /// <summary>字体名称</summary>
    private readonly string _fontName = string.IsNullOrWhiteSpace(fontName) ? string.Empty : fontName;

    /// <summary>字号大小</summary>
    private readonly int _fontSize = fontSize;

    /// <summary>主文本颜色</summary>
    private readonly string _primaryColour = string.IsNullOrWhiteSpace(primaryColour) ? string.Empty : primaryColour;

    /// <summary>次要填充颜色</summary>
    private readonly string _secondaryColour =
        string.IsNullOrWhiteSpace(secondaryColour) ? string.Empty : secondaryColour;

    /// <summary>文字描边颜色</summary>
    private readonly string _outlineColour = string.IsNullOrWhiteSpace(outlineColour) ? string.Empty : outlineColour;

    /// <summary>阴影填充颜色</summary>
    private readonly string _backColour = string.IsNullOrWhiteSpace(backColour) ? string.Empty : backColour;

    /// <summary>加粗开关</summary>
    private readonly bool _bold = bold;

    /// <summary>斜体开关</summary>
    private readonly bool _italic = italic;

    /// <summary>下划线开关</summary>
    private readonly bool _underline = underline;

    /// <summary>删除线开关</summary>
    private readonly bool _strikeOut = strikeOut;

    /// <summary>横向缩放比例</summary>
    private readonly float _scaleX = scaleX;

    /// <summary>纵向缩放比例</summary>
    private readonly float _scaleY = scaleY;

    /// <summary>字符间距</summary>
    private readonly float _spacing = spacing;

    /// <summary>文字旋转角度</summary>
    private readonly float _angle = angle;

    /// <summary>描边渲染模式</summary>
    private readonly int _borderStyle = borderStyle;

    /// <summary>描边宽度</summary>
    private readonly float _outline = outline;

    /// <summary>阴影宽度</summary>
    private readonly float _shadow = shadow;

    /// <summary>字幕对齐位置</summary>
    private readonly int _alignment = alignment;

    /// <summary>左侧整体边距</summary>
    private readonly int _marginL = marginL;

    /// <summary>右侧整体边距</summary>
    private readonly int _marginR = marginR;

    /// <summary>垂直整体边距</summary>
    private readonly int _marginV = marginV;

    /// <summary>文本编码标识</summary>
    private readonly int _encoding = encoding;

    // ── 公共只读属性：Newtonsoft 序列化/反序列化与渲染端读取。
    // JSON 字段名与 Studio 前端 IAssStyle 契约对齐（fontname/fontsize 等小写形式）。
    /// <summary>样式名称。</summary>
    [JsonProperty("name")] public string Name => _name;
    /// <summary>字体名称。</summary>
    [JsonProperty("fontname")] public string FontName => _fontName;
    /// <summary>字号（像素）。</summary>
    [JsonProperty("fontsize")] public int FontSize => _fontSize;
    /// <summary>主文字颜色（ASS 格式，如 &amp;H00FFFFFF）。</summary>
    [JsonProperty("primaryColour")] public string PrimaryColour => _primaryColour;
    /// <summary>次要填充色（卡拉OK 未演唱段颜色）。</summary>
    [JsonProperty("secondaryColour")] public string SecondaryColour => _secondaryColour;
    /// <summary>描边颜色。</summary>
    [JsonProperty("outlineColour")] public string OutlineColour => _outlineColour;
    /// <summary>阴影（背景）颜色。</summary>
    [JsonProperty("backColour")] public string BackColour => _backColour;
    /// <summary>是否加粗。</summary>
    [JsonProperty("bold")] public bool Bold => _bold;
    /// <summary>是否斜体。</summary>
    [JsonProperty("italic")] public bool Italic => _italic;
    /// <summary>是否下划线。</summary>
    [JsonProperty("underline")] public bool Underline => _underline;
    /// <summary>是否删除线。</summary>
    [JsonProperty("strikeOut")] public bool StrikeOut => _strikeOut;
    /// <summary>横向缩放百分比。</summary>
    [JsonProperty("scaleX")] public float ScaleX => _scaleX;
    /// <summary>纵向缩放百分比。</summary>
    [JsonProperty("scaleY")] public float ScaleY => _scaleY;
    /// <summary>字符间距（像素）。</summary>
    [JsonProperty("spacing")] public float Spacing => _spacing;
    /// <summary>文字旋转角度（度，逆时针为正）。</summary>
    [JsonProperty("angle")] public float Angle => _angle;
    /// <summary>描边渲染模式（1=描边+不透明底，3=不透明框）。</summary>
    [JsonProperty("borderStyle")] public int BorderStyle => _borderStyle;
    /// <summary>描边粗细。</summary>
    [JsonProperty("outline")] public float Outline => _outline;
    /// <summary>阴影深度。</summary>
    [JsonProperty("shadow")] public float Shadow => _shadow;
    /// <summary>对齐方式（ASS 对齐编号 1~9）。</summary>
    [JsonProperty("alignment")] public int Alignment => _alignment;
    /// <summary>左侧安全边距。</summary>
    [JsonProperty("marginL")] public int MarginL => _marginL;
    /// <summary>右侧安全边距。</summary>
    [JsonProperty("marginR")] public int MarginR => _marginR;
    /// <summary>垂直安全边距。</summary>
    [JsonProperty("marginV")] public int MarginV => _marginV;
    /// <summary>文本编码 ID。</summary>
    [JsonProperty("encoding")] public int Encoding => _encoding;

    /// <summary>输出ASS标准Style行</summary>
    public override string ToString()
    {
        return
            $"Style: {_name},{_fontName},{_fontSize},{_primaryColour},{_secondaryColour},{_outlineColour},{_backColour},{(_bold ? -1 : 0)},{(_italic ? -1 : 0)},{(_underline ? -1 : 0)},{(_strikeOut ? -1 : 0)},{_scaleX},{_scaleY},{_spacing},{_angle},{_borderStyle},{_outline},{_shadow},{_alignment},{_marginL},{_marginR},{_marginV},{_encoding}";
    }

    /// <summary>按样式名判等</summary>
    public override bool Equals(object? obj)
    {
        if (obj is AssStyle other) return _name == other._name;
        return false;
    }

    /// <summary>以样式名生成哈希码</summary>
    public override int GetHashCode()
    {
        return _name?.GetHashCode() ?? 0;
    }
}
