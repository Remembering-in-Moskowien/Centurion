namespace Centurion.Models.Workflow;

/// <summary>
/// 语音活动段（VAD 检出）：源音频时间轴上的起止（毫秒）。
/// 聚合后 <see cref="AggStartMs"/> 记录该段在聚合音频中的起始位置（毫秒），
/// 供转录结果从聚合轴还原回源时间轴。
/// </summary>
public class VoiceSegment
{
    /// <summary>段在源音频上的起始时间（毫秒）。</summary>
    public double StartMs { get; init; }
    /// <summary>段在源音频上的结束时间（毫秒）。</summary>
    public double EndMs { get; init; }
    /// <summary>段在聚合音频中的起始位置（毫秒）；VAD 聚合阶段回填。</summary>
    public double AggStartMs { get; set; }
    /// <summary>段平均 RMS 能量（dB，诊断用）。</summary>
    public double RmsDb { get; init; }
}
