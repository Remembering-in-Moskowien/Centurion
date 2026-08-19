using System.Text.RegularExpressions;
using Centurion.Core.Tools;

namespace Centurion.Core.Models;

/// <summary>
/// 构建器基础抽象类，提供流式赋值通用封装
/// </summary>
/// <typeparam name="TBuilder">当前构建器派生类类型</typeparam>
/// <typeparam name="TProduct">构建完成输出的实体类型</typeparam>
public abstract class BuilderBase<TBuilder, TProduct>
    where TBuilder : BuilderBase<TBuilder, TProduct>
{
    /// <summary>
    /// 为内部字段赋值，返回自身实现链式调用
    /// </summary>
    /// <typeparam name="TValue">字段值类型</typeparam>
    /// <param name="field">待赋值字段引用</param>
    /// <param name="value">新值</param>
    /// <returns>当前构建器实例</returns>
    protected TBuilder Set<TValue>(ref TValue field, TValue value)
    {
        field = value;
        return (TBuilder)this;
    }

    /// <summary>
    /// 执行构建，生成最终实体对象
    /// </summary>
    /// <returns>构建完成的模型</returns>
    public abstract TProduct Build();
}

/// <summary>
/// ASS字幕行流式构建器，快速创建Dialogue/Comment行
/// </summary>
public partial class AssSubLineBuilder : BuilderBase<AssSubLineBuilder, AssSubLine>
{
    /// <summary>是否为注释行</summary>
    private bool _isComment;

    /// <summary>图层序号</summary>
    private int _layer;

    /// <summary>起始毫秒</summary>
    private long _start;

    /// <summary>结束毫秒</summary>
    private long _end;

    /// <summary>绑定样式名</summary>
    private string _style = string.Empty;

    /// <summary>说话人名称</summary>
    private string _name = string.Empty;

    /// <summary>左侧边距</summary>
    private int _marginL;

    /// <summary>右侧边距</summary>
    private int _marginR;

    /// <summary>垂直边距</summary>
    private int _marginV;

    /// <summary>特效字符串</summary>
    private string _effect = string.Empty;

    /// <summary>字幕正文</summary>
    private string _text = string.Empty;

    /// <summary>是否注释行</summary>
    public bool IsComment => _isComment;

    /// <summary>图层层级</summary>
    public int Layer => _layer;

    /// <summary>起始时间(毫秒)</summary>
    public long Start => _start;

    /// <summary>结束时间(毫秒)</summary>
    public long End => _end;

    /// <summary>样式名称</summary>
    public string Style => _style;

    /// <summary>说话人</summary>
    public string Name => _name;

    /// <summary>左留白</summary>
    public int MarginL => _marginL;

    /// <summary>右留白</summary>
    public int MarginR => _marginR;

    /// <summary>垂直留白</summary>
    public int MarginV => _marginV;

    /// <summary>ASS特效</summary>
    public string Effect => _effect;

    /// <summary>字幕文本</summary>
    public string Text => _text;

    /// <summary>设置是否为注释行</summary>
    public AssSubLineBuilder WithComment(bool value)
    {
        return Set(ref _isComment, value);
    }

    /// <summary>设置图层序号</summary>
    public AssSubLineBuilder WithLayer(int value)
    {
        return Set(ref _layer, value);
    }

    /// <summary>设置起始毫秒时间</summary>
    public AssSubLineBuilder WithStart(long value)
    {
        return Set(ref _start, value);
    }

    /// <summary>设置结束毫秒时间</summary>
    public AssSubLineBuilder WithEnd(long value)
    {
        return Set(ref _end, value);
    }

    /// <summary>绑定样式名称</summary>
    public AssSubLineBuilder WithStyle(string value)
    {
        return Set(ref _style, string.IsNullOrWhiteSpace(value) ? string.Empty : value);
    }

    /// <summary>设置说话人名称</summary>
    public AssSubLineBuilder WithName(string value)
    {
        return Set(ref _name, string.IsNullOrWhiteSpace(value) ? string.Empty : value);
    }

    /// <summary>设置左侧边距</summary>
    public AssSubLineBuilder WithMarginL(int value)
    {
        return Set(ref _marginL, value);
    }

    /// <summary>设置右侧边距</summary>
    public AssSubLineBuilder WithMarginR(int value)
    {
        return Set(ref _marginR, value);
    }

    /// <summary>设置垂直边距</summary>
    public AssSubLineBuilder WithMarginV(int value)
    {
        return Set(ref _marginV, value);
    }

    /// <summary>设置字幕特效</summary>
    public AssSubLineBuilder WithEffect(string value)
    {
        return Set(ref _effect, string.IsNullOrWhiteSpace(value) ? string.Empty : value);
    }

    /// <summary>设置字幕正文文本</summary>
    public AssSubLineBuilder WithText(string value)
    {
        return Set(ref _text, string.IsNullOrWhiteSpace(value) ? string.Empty : value);
    }

    /// <summary>
    /// 从原始ASS行文本解析生成构建器实例
    /// </summary>
    /// <param name="content">原始Dialogue/Comment行</param>
    /// <returns>填充完成的构建器</returns>
    /// <exception cref="FormatException">文本格式不合法时抛出</exception>
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

    /// <summary>根据当前配置生成AssSubLine实例</summary>
    public override AssSubLine Build()
    {
        return new AssSubLine(_isComment, _layer, _start, _end, _style, _name, _marginL, _marginR, _marginV, _effect,
            _text);
    }

    /// <summary>匹配ASS Dialogue/Comment单行正则</summary>
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

    public string Name => _name;
    public string FontName => _fontName;
    public int FontSize => _fontSize;
    public string PrimaryColour => _primaryColour;
    public string SecondaryColour => _secondaryColour;
    public string OutlineColour => _outlineColour;
    public string BackColour => _backColour;
    public bool Bold => _bold;
    public bool Italic => _italic;
    public bool Underline => _underline;
    public bool StrikeOut => _strikeOut;
    public float ScaleX => _scaleX;
    public float ScaleY => _scaleY;
    public float Spacing => _spacing;
    public float Angle => _angle;
    public int BorderStyle => _borderStyle;
    public float Outline => _outline;
    public float Shadow => _shadow;
    public int Alignment => _alignment;
    public int MarginL => _marginL;
    public int MarginR => _marginR;
    public int MarginV => _marginV;
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

    /// <summary>填充一套默认标准字幕样式</summary>
    public AssStyleBuilder WithDefaultValues()
    {
        return WithName("Default")
            .WithFontName("Arial")
            .WithFontSize(55)
            .WithPrimaryColour("&H00FFFFFF")
            .WithSecondaryColour("&H000000FF")
            .WithOutlineColour("&H00000000")
            .WithBackColour("&H00000000")
            .WithBold(false)
            .WithItalic(false)
            .WithUnderline(false)
            .WithStrikeOut(false)
            .WithScaleX(100.0f)
            .WithScaleY(100.0f)
            .WithSpacing(0.0f)
            .WithAngle(0.0f)
            .WithBorderStyle(1)
            .WithOutline(2.0f)
            .WithShadow(0.0f)
            .WithAlignment(2)
            .WithMarginL(10)
            .WithMarginR(10)
            .WithMarginV(35)
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

/// <summary>
/// ASS完整字幕文档构建器，组装脚本信息、样式、对话行
/// </summary>
public partial class AssSubBuilder : BuilderBase<AssSubBuilder, AssSub>
{
    private string _title = string.Empty;
    private string _scriptType = string.Empty;
    private string _wrapStyle = string.Empty;
    private string _collisions = string.Empty;
    private string _playResX = string.Empty;
    private string _playResY = string.Empty;
    private float _timer;
    private List<AssStyle> _styles = [];
    private List<AssSubLine> _lines = [];

    public string Title => _title;
    public string ScriptType => _scriptType;
    public string WrapStyle => _wrapStyle;
    public string Collisions => _collisions;
    public string PlayResX => _playResX;
    public string PlayResY => _playResY;
    public float Timer => _timer;
    public List<AssStyle> Styles => _styles;
    public List<AssSubLine> Lines => _lines;

    /// <summary>设置字幕标题</summary>
    public AssSubBuilder WithTitle(string value)
    {
        return Set(ref _title, string.IsNullOrWhiteSpace(value) ? string.Empty : value);
    }

    /// <summary>设置脚本版本</summary>
    public AssSubBuilder WithScriptType(string value)
    {
        return Set(ref _scriptType, string.IsNullOrWhiteSpace(value) ? string.Empty : value);
    }

    /// <summary>设置自动换行规则</summary>
    public AssSubBuilder WithWrapStyle(string value)
    {
        return Set(ref _wrapStyle, string.IsNullOrWhiteSpace(value) ? string.Empty : value);
    }

    /// <summary>设置字幕重叠处理策略</summary>
    public AssSubBuilder WithCollisions(string value)
    {
        return Set(ref _collisions, string.IsNullOrWhiteSpace(value) ? string.Empty : value);
    }

    /// <summary>设置基准分辨率宽度</summary>
    public AssSubBuilder WithPlayResX(string value)
    {
        return Set(ref _playResX, string.IsNullOrWhiteSpace(value) ? string.Empty : value);
    }

    /// <summary>设置基准分辨率高度</summary>
    public AssSubBuilder WithPlayResY(string value)
    {
        return Set(ref _playResY, string.IsNullOrWhiteSpace(value) ? string.Empty : value);
    }

    /// <summary>设置时间缩放系数</summary>
    public AssSubBuilder WithTimer(float value)
    {
        return Set(ref _timer, value);
    }

    /// <summary>批量设置样式集合</summary>
    public AssSubBuilder WithStyles(List<AssStyle> value)
    {
        return Set(ref _styles, value ?? []);
    }

    /// <summary>批量设置字幕行集合</summary>
    public AssSubBuilder WithLines(List<AssSubLine> value)
    {
        return Set(ref _lines, value ?? []);
    }

    /// <summary>是否自动添加Default默认样式</summary>
    public AssSubBuilder WithAddDefaultStyle()
    {
        return WithStyles([new AssStyleBuilder().WithDefaultValues().Build()]);
    }

    /// <summary>填充一套标准ASS默认脚本配置</summary>
    public AssSubBuilder WithDefaultValues()
    {
        return WithTitle("Default AssSub file")
            .WithScriptType("v4.00+")
            .WithWrapStyle("0")
            .WithCollisions("Normal")
            .WithPlayResX("1920")
            .WithPlayResY("1080")
            .WithTimer(100.0f)
            .WithStyles([])
            .WithLines([])
            .WithAddDefaultStyle();
    }

    /// <summary>从完整ASS文本解析生成文档构建器</summary>
    /// <param name="content">完整ASS文件字符串</param>
    /// <returns>填充完成的构建器</returns>
    /// <exception cref="FormatException">文件结构非法</exception>
    public static AssSubBuilder FromContent(string content)
    {
        var builder = new AssSubBuilder();
        var scriptInfoMatch = ScriptInfoBlockRegex().Match(content);
        if (scriptInfoMatch.Success)
        {
            var scriptInfoContent = scriptInfoMatch.Groups[1].Value;
            builder = builder
                .WithTitle(SubTools.GetText(TitleRegex(), scriptInfoContent, "默认字幕"))
                .WithScriptType(SubTools.GetText(ScriptTypeRegex(), scriptInfoContent, "v4.00+"))
                .WithWrapStyle(SubTools.GetText(WrapStyleRegex(), scriptInfoContent, "0"))
                .WithCollisions(SubTools.GetText(CollisionsRegex(), scriptInfoContent, "Normal"))
                .WithPlayResX(SubTools.GetText(PlayResXRegex(), scriptInfoContent, "1920"))
                .WithPlayResY(SubTools.GetText(PlayResYRegex(), scriptInfoContent, "1080"));
            var timerStr = SubTools.GetText(TimerRegex(), scriptInfoContent);
            if (float.TryParse(timerStr, out var t))
                builder = builder.WithTimer(t);
        }
        else
        {
            builder = builder.WithDefaultValues();
        }

        // 解析所有样式
        var styles = new List<AssStyle>();
        var stylesMatch = V4StyleBlockRegex().Match(content);
        if (stylesMatch.Success)
        {
            var stylesContent = stylesMatch.Groups[1].Value;
            foreach (var line in stylesContent.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                if (line.StartsWith("Style:"))
                    styles.Add(AssStyleBuilder.FromContent(line).Build());

            styles = [.. styles.Distinct()];
        }

        builder = builder.WithStyles(styles).WithAddDefaultStyle();

        // 解析所有对话行并按起始时间排序
        var lines = new List<AssSubLine>();
        var eventsMatch = EventsBlockRegex().Match(content);
        if (eventsMatch.Success)
        {
            var eventsContent = eventsMatch.Groups[1].Value;
            foreach (var line in eventsContent.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                if (line.StartsWith("Dialogue:") || line.StartsWith("Comment:"))
                    lines.Add(AssSubLineBuilder.FromContent(line).Build());

            lines = [.. lines.OrderBy(l => l.GetStart())];
        }

        return builder.WithLines(lines);
    }

    /// <summary>直接读取ASS文件并解析为构建器</summary>
    public static AssSubBuilder FromFile(string path)
    {
        return FromContent(File.ReadAllText(path));
    }

    /// <summary>组装所有配置，生成完整AssSub字幕文档</summary>
    public override AssSub Build()
    {
        return new AssSub(
            _title,
            _scriptType,
            _wrapStyle,
            _collisions,
            _playResX,
            _playResY,
            _timer,
            [.. _styles],
            [.. _lines]
        );
    }

    // 脚本信息块正则
    [GeneratedRegex(@"Title:\s*(.*)", RegexOptions.None)]
    private static partial Regex TitleRegex();

    [GeneratedRegex(@"ScriptType:\s*(.*)", RegexOptions.None)]
    private static partial Regex ScriptTypeRegex();

    [GeneratedRegex(@"WrapStyle:\s*(.*)", RegexOptions.None)]
    private static partial Regex WrapStyleRegex();

    [GeneratedRegex(@"Collisions:\s*(.*)", RegexOptions.None)]
    private static partial Regex CollisionsRegex();

    [GeneratedRegex(@"PlayResX:\s*(.*)", RegexOptions.None)]
    private static partial Regex PlayResXRegex();

    [GeneratedRegex(@"PlayResY:\s*(.*)", RegexOptions.None)]
    private static partial Regex PlayResYRegex();

    [GeneratedRegex(@"Timer:\s*(.*)", RegexOptions.None)]
    private static partial Regex TimerRegex();

    [GeneratedRegex(@"\[Script Info\](.*?)\[V4\+ Styles\]", RegexOptions.Singleline)]
    private static partial Regex ScriptInfoBlockRegex();

    [GeneratedRegex(@"\[V4\+ Styles\](.*?)\[Events\]", RegexOptions.Singleline)]
    private static partial Regex V4StyleBlockRegex();

    [GeneratedRegex(@"\[Events\](.*)", RegexOptions.Singleline)]
    private static partial Regex EventsBlockRegex();
}