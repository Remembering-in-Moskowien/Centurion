using System.Text;
using Centurion.Core.Tools;

namespace Centurion.Core.Models;

/// <summary>
/// 单条ASS对话/注释字幕行实体
/// </summary>
/// <param name="isComment">是否为注释行，true=Comment，false=Dialogue</param>
/// <param name="layer">图层序号</param>
/// <param name="start">起始毫秒</param>
/// <param name="end">结束毫秒</param>
/// <param name="style">样式名称</param>
/// <param name="name">说话人名称</param>
/// <param name="marginL">左边距</param>
/// <param name="marginR">右边距</param>
/// <param name="marginV">垂直边距</param>
/// <param name="effect">特效标签</param>
/// <param name="text">字幕文本</param>
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
    /// <summary>是否注释行</summary>
    private readonly bool _isComment = isComment;

    /// <summary>图层层级</summary>
    private readonly int _layer = layer;

    /// <summary>起始时间(毫秒)</summary>
    private readonly long _start = start;

    /// <summary>结束时间(毫秒)</summary>
    private readonly long _end = end;

    /// <summary>绑定样式名</summary>
    private readonly string _style = string.IsNullOrWhiteSpace(style) ? string.Empty : style;

    /// <summary>角色/说话人名</summary>
    private readonly string _name = string.IsNullOrWhiteSpace(name) ? string.Empty : name;

    /// <summary>左侧留白</summary>
    private readonly int _marginL = marginL;

    /// <summary>右侧留白</summary>
    private readonly int _marginR = marginR;

    /// <summary>垂直留白</summary>
    private readonly int _marginV = marginV;

    /// <summary>ASS特效字符串</summary>
    private readonly string _effect = string.IsNullOrWhiteSpace(effect) ? string.Empty : effect;

    /// <summary>字幕正文</summary>
    private readonly string _text = string.IsNullOrWhiteSpace(text) ? string.Empty : text;

    public long GetStart()
    {
        return _start;
    }

    /// <summary>
    /// 输出标准ASS行文本
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

/// <summary>
/// 完整ASS字幕文档模型，包含脚本信息、样式集合、对话行集合
/// </summary>
/// <param name="title">字幕标题</param>
/// <param name="scriptType">脚本版本标识</param>
/// <param name="wrapStyle">自动换行规则</param>
/// <param name="collisions">重叠碰撞处理</param>
/// <param name="playResX">视频基准宽度</param>
/// <param name="playResY">视频基准高度</param>
/// <param name="timer">时间缩放系数</param>
/// <param name="styles">样式列表</param>
/// <param name="lines">对话/注释行列表</param>
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
    /// <summary>字幕文档标题</summary>
    private readonly string _title = string.IsNullOrWhiteSpace(title) ? string.Empty : title;

    /// <summary>脚本类型版本</summary>
    private readonly string _scriptType = string.IsNullOrWhiteSpace(scriptType) ? string.Empty : scriptType;

    /// <summary>自动换行模式</summary>
    private readonly string _wrapStyle = string.IsNullOrWhiteSpace(wrapStyle) ? string.Empty : wrapStyle;

    /// <summary>字幕重叠处理策略</summary>
    private readonly string _collisions = string.IsNullOrWhiteSpace(collisions) ? string.Empty : collisions;

    /// <summary>基准分辨率宽度</summary>
    private readonly string _playResX = string.IsNullOrWhiteSpace(playResX) ? string.Empty : playResX;

    /// <summary>基准分辨率高度</summary>
    private readonly string _playResY = string.IsNullOrWhiteSpace(playResY) ? string.Empty : playResY;

    /// <summary>时间计时器缩放值</summary>
    private readonly float _timer = timer;

    /// <summary>所有样式定义集合</summary>
    private readonly List<AssStyle> _styles = styles ?? [];

    /// <summary>所有字幕对话/注释行集合</summary>
    private readonly List<AssSubLine> _lines = lines ?? [];

    /// <summary>完整输出标准ASS文件文本</summary>
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
        foreach (var style in _styles) sb.AppendLine(style.ToString());
        sb.AppendLine();
        sb.AppendLine("[Events]");
        foreach (var line in _lines) sb.AppendLine(line.ToString());
        return sb.ToString();
    }
}