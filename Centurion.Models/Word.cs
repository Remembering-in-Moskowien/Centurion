namespace Centurion.Models;

/// <summary>一个词级别的转录单元，携带文本、时间轴、说话人、词性标注与对齐状态。</summary>
public class Word
{
    /// <summary>词的文本。</summary>
    public required string Text { get; set; }
    /// <summary>词起始时间（毫秒）。</summary>
    public double Start { get; set; }
    /// <summary>词结束时间（毫秒）。</summary>
    public double End { get; set; }
    /// <summary>该词所属的说话人标识，未做说话人分离时可为空串。</summary>
    public required string Speaker { get; set; }
    /// <summary>词性标注（POS tag），未标注时为 null。</summary>
    public string? PosTag { get; set; }
    /// <summary>该词与脚本的对齐匹配状态，默认为 <see cref="MappingStatus.Matched"/>。</summary>
    public MappingStatus Status { get; set; } = MappingStatus.Matched;
}
