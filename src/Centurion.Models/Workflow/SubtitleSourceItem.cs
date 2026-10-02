using Centurion.Models.Ass;

namespace Centurion.Models.Workflow;

/// <summary>
/// combine 命令的单个字幕输入源解析结果：一个来源（媒体内的一条字幕轨，
/// 或一个裸字幕文件）解析出的句子集合与其 ASS 样式表（仅 ASS/SSA 来源携带）。
/// 仅在进程内由算子间传递，不写入 IR。
/// </summary>
public sealed class SubtitleSourceItem
{
    /// <summary>来源标识（写入句子 <see cref="Sentence.Source"/>，如 "video.mkv #track 2 (eng)" 或 "chs.ass"）。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>该来源解析出的句子（尚未标记 Source，由合并算子统一填充）。</summary>
    public List<Sentence> Sentences { get; init; } = [];

    /// <summary>该来源携带的 ASS 样式表（非 ASS 输入时为空）。</summary>
    public List<AssStyle> Styles { get; init; } = [];
}
