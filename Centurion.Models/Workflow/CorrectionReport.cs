namespace Centurion.Models.Workflow;

/// <summary>以字幕/脚本校正识别结果时采用的校正策略。</summary>
public enum CorrectionStrategy
{
    /// <summary>仅校正时间轴，不改写文本。</summary>
    TimelineOnly,
    /// <summary>仅校正文本，不调整时间轴。</summary>
    TextOnly,
    /// <summary>同时校正时间轴与文本。</summary>
    Both
}

/// <summary>一次校正（对齐）操作的统计报告。</summary>
public sealed class CorrectionReport
{
    /// <summary>参与校正的句子总数。</summary>
    public int TotalSentences { get; set; }
    /// <summary>被改写过文本的句子数。</summary>
    public int TextCorrected { get; set; }
    /// <summary>时间轴被平移过的句子数。</summary>
    public int TimelineShifted { get; set; }
    /// <summary>未能与脚本对齐的句子数。</summary>
    public int Unmatched { get; set; }
    /// <summary>对齐残差的平均漂移量（毫秒）。</summary>
    public double AverageDriftMs { get; set; }
    /// <summary>脚本文本被覆盖的比例（0-1）。</summary>
    public double TextCoverage { get; set; }
    /// <summary>本次校正耗时。</summary>
    public TimeSpan Elapsed { get; set; }
}
