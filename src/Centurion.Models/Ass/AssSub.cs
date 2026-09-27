using System.Text;

namespace Centurion.Models.Ass;

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
        // 添加 Style 格式行
        sb.AppendLine("Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding");
        foreach (var style in _styles)
            sb.AppendLine(style.ToString());
        sb.AppendLine();

        sb.AppendLine("[Events]");
        // 添加 Event 格式行
        sb.AppendLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");
        foreach (var line in _lines)
            sb.AppendLine(line.ToString());

        return sb.ToString();
    }
}
