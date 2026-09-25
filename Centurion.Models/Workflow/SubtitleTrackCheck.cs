namespace Centurion.Models.Workflow;

/// <summary>
/// 媒体文件中的单条轨道信息（由 mkvmerge -i 解析得到）。
/// </summary>
public sealed class MkvTrackInfo
{
    /// <summary>轨道 ID（mkvmerge 输出中的 Track ID）。</summary>
    public int TrackId { get; init; }

    /// <summary>轨道类型：video / audio / subtitles。</summary>
    public string Type { get; init; } = "";

    /// <summary>轨道编码（如 V_MPEG4/ISO/AVC、S_TEXT/UTF8）。</summary>
    public string Codec { get; init; } = "";

    /// <summary>轨道语言（如 eng、und），无时为 null。</summary>
    public string? Language { get; init; }

    /// <summary>轨道名称（如 "English"），无时为 null。</summary>
    public string? Name { get; init; }

    /// <summary>是否为字幕轨。</summary>
    public bool IsSubtitle => Type.Equals("subtitles", StringComparison.OrdinalIgnoreCase);

    /// <summary>简洁的可读描述（用于警告文案）。</summary>
    public string Summary =>
        $"ID {TrackId} ({Codec})" + (Language is null ? "" : $", lang {Language}");
}

/// <summary>
/// 字幕轨检查结果：mkvtoolnix（mkvmerge -i）对输入媒体的轨道探测结论。
/// </summary>
public sealed class SubtitleTrackCheckResult
{
    /// <summary>被检查的媒体文件路径。</summary>
    public string SourceFile { get; init; } = "";

    /// <summary>mkvmerge 是否成功运行并解析出轨道。</summary>
    public bool Checked { get; init; }

    /// <summary>媒体中是否存在字幕轨。</summary>
    public bool HasSubtitleTracks { get; init; }

    /// <summary>媒体中的全部轨道。</summary>
    public List<MkvTrackInfo> AllTracks { get; init; } = [];

    /// <summary>媒体中的字幕轨子集。</summary>
    public List<MkvTrackInfo> SubtitleTracks { get; init; } = [];

    /// <summary>检查被跳过或失败时的说明（如 mkvmerge 缺失、非容器文件）。</summary>
    public string? Message { get; init; }
}
