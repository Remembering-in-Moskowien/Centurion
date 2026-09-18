using System.Text.RegularExpressions;
using Centurion.Core.Utils;

namespace Centurion.Core.Models.Ass;

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
