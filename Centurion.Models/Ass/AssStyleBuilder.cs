using System.Text.RegularExpressions;

namespace Centurion.Models.Ass;

/// <summary>
/// ASS样式流式构建器，快速生成Style定义
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

    /// <summary>样式名称，用于在文档中引用该样式。</summary>
    public string Name => _name;
    /// <summary>字体名称。</summary>
    public string FontName => _fontName;
    /// <summary>字号（像素）。</summary>
    public int FontSize => _fontSize;
    /// <summary>主文字颜色，ASS 格式如 &amp;H00FFFFFF。</summary>
    public string PrimaryColour => _primaryColour;
    /// <summary>次要填充色，卡拉OK 未演唱段使用的颜色。</summary>
    public string SecondaryColour => _secondaryColour;
    /// <summary>描边颜色。</summary>
    public string OutlineColour => _outlineColour;
    /// <summary>阴影（背景）颜色。</summary>
    public string BackColour => _backColour;
    /// <summary>是否加粗。</summary>
    public bool Bold => _bold;
    /// <summary>是否斜体。</summary>
    public bool Italic => _italic;
    /// <summary>是否下划线。</summary>
    public bool Underline => _underline;
    /// <summary>是否删除线。</summary>
    public bool StrikeOut => _strikeOut;
    /// <summary>横向缩放百分比。</summary>
    public float ScaleX => _scaleX;
    /// <summary>纵向缩放百分比。</summary>
    public float ScaleY => _scaleY;
    /// <summary>字符间距（像素）。</summary>
    public float Spacing => _spacing;
    /// <summary>文字旋转角度（度，逆时针为正）。</summary>
    public float Angle => _angle;
    /// <summary>描边渲染模式（1=描边+不透明底，3=不透明框）。</summary>
    public int BorderStyle => _borderStyle;
    /// <summary>描边粗细。</summary>
    public float Outline => _outline;
    /// <summary>阴影深度。</summary>
    public float Shadow => _shadow;
    /// <summary>对齐方式（ASS 对齐编号，1-9 对应九宫格）。</summary>
    public int Alignment => _alignment;
    /// <summary>左侧安全边距。</summary>
    public int MarginL => _marginL;
    /// <summary>右侧安全边距。</summary>
    public int MarginR => _marginR;
    /// <summary>垂直安全边距。</summary>
    public int MarginV => _marginV;
    /// <summary>文本编码 ID（如 1 表示默认系统编码）。</summary>
    public int Encoding => _encoding;

    /// <summary>设置样式名称</summary>
    public AssStyleBuilder WithName(string value)
    {
        return Set(ref _name, string.IsNullOrWhiteSpace(value) ? string.Empty : value);
    }

    /// <summary>设置字体名</summary>
    public AssStyleBuilder WithFontName(string value)
    {
        return Set(ref _fontName, string.IsNullOrWhiteSpace(value) ? string.Empty : value);
    }

    /// <summary>设置字号</summary>
    public AssStyleBuilder WithFontSize(int value)
    {
        return Set(ref _fontSize, value);
    }

    /// <summary>设置主文字颜色</summary>
    public AssStyleBuilder WithPrimaryColour(string value)
    {
        return Set(ref _primaryColour, string.IsNullOrWhiteSpace(value) ? string.Empty : value);
    }

    /// <summary>设置次要填充色</summary>
    public AssStyleBuilder WithSecondaryColour(string value)
    {
        return Set(ref _secondaryColour, string.IsNullOrWhiteSpace(value) ? string.Empty : value);
    }

    /// <summary>设置描边颜色</summary>
    public AssStyleBuilder WithOutlineColour(string value)
    {
        return Set(ref _outlineColour, string.IsNullOrWhiteSpace(value) ? string.Empty : value);
    }

    /// <summary>设置阴影颜色</summary>
    public AssStyleBuilder WithBackColour(string value)
    {
        return Set(ref _backColour, string.IsNullOrWhiteSpace(value) ? string.Empty : value);
    }

    /// <summary>开启/关闭加粗</summary>
    public AssStyleBuilder WithBold(bool value)
    {
        return Set(ref _bold, value);
    }

    /// <summary>开启/关闭斜体</summary>
    public AssStyleBuilder WithItalic(bool value)
    {
        return Set(ref _italic, value);
    }

    /// <summary>开启/关闭下划线</summary>
    public AssStyleBuilder WithUnderline(bool value)
    {
        return Set(ref _underline, value);
    }

    /// <summary>开启/关闭删除线</summary>
    public AssStyleBuilder WithStrikeOut(bool value)
    {
        return Set(ref _strikeOut, value);
    }

    /// <summary>横向缩放比例</summary>
    public AssStyleBuilder WithScaleX(float value)
    {
        return Set(ref _scaleX, value);
    }

    /// <summary>纵向缩放比例</summary>
    public AssStyleBuilder WithScaleY(float value)
    {
        return Set(ref _scaleY, value);
    }

    /// <summary>字间距</summary>
    public AssStyleBuilder WithSpacing(float value)
    {
        return Set(ref _spacing, value);
    }

    /// <summary>文字旋转角度</summary>
    public AssStyleBuilder WithAngle(float value)
    {
        return Set(ref _angle, value);
    }

    /// <summary>描边渲染模式</summary>
    public AssStyleBuilder WithBorderStyle(int value)
    {
        return Set(ref _borderStyle, value);
    }

    /// <summary>描边粗细</summary>
    public AssStyleBuilder WithOutline(float value)
    {
        return Set(ref _outline, value);
    }

    /// <summary>阴影厚度</summary>
    public AssStyleBuilder WithShadow(float value)
    {
        return Set(ref _shadow, value);
    }

    /// <summary>字幕对齐方式</summary>
    public AssStyleBuilder WithAlignment(int value)
    {
        return Set(ref _alignment, value);
    }

    /// <summary>左侧整体边距</summary>
    public AssStyleBuilder WithMarginL(int value)
    {
        return Set(ref _marginL, value);
    }

    /// <summary>右侧整体边距</summary>
    public AssStyleBuilder WithMarginR(int value)
    {
        return Set(ref _marginR, value);
    }

    /// <summary>垂直整体边距</summary>
    public AssStyleBuilder WithMarginV(int value)
    {
        return Set(ref _marginV, value);
    }

    /// <summary>文本编码ID</summary>
    public AssStyleBuilder WithEncoding(int value)
    {
        return Set(ref _encoding, value);
    }

    /// <summary>
    /// 填充一套默认主字幕样式（黑体风格，参照 Theme.ass 的视频语言 eng 样式）：
    /// 使用 Arial 粗体（Windows/macOS 原生自带；Linux 由 Liberation Sans 度量兼容替代），
    /// 半透明描边 + 阴影，底部居中、垂直边距 100。
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
    /// 填充一套次字幕样式（中文黑体风格，参照 Theme.ass 的目标语言 chi 样式）：
    /// 使用微软雅黑（Windows 中文系统自带；macOS 回退苹方 PingFang SC、Linux 回退 Noto Sans CJK SC，
    /// 均为现代黑体观感接近），贴底显示（垂直边距 28），用于双语字幕的译文行。
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

    /// <summary>从原始Style文本解析生成样式构建器</summary>
    /// <param name="content">原始Style行</param>
    /// <returns>填充完成的构建器</returns>
    /// <exception cref="FormatException">格式非法抛出</exception>
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
            throw new FormatException("Style行不符合ASS标准格式");
        return builder;
    }

    /// <summary>生成AssStyle样式实例</summary>
    public override AssStyle Build()
    {
        return new AssStyle(_name, _fontName, _fontSize, _primaryColour, _secondaryColour, _outlineColour, _backColour,
            _bold, _italic, _underline, _strikeOut, _scaleX, _scaleY, _spacing, _angle, _borderStyle, _outline, _shadow,
            _alignment, _marginL, _marginR, _marginV, _encoding);
    }

    /// <summary>匹配ASS Style定义行正则</summary>
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
