using Centurion.Models.Ass;

namespace Centurion.Models.Ass;

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
