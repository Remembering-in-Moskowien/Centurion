namespace Centurion.Models.Workflow;

/// <summary>
/// dub（媒体译制）管道中的单个合成段：目标文本 + 说话人参考 + 时间轴约束 + 合成产物。
/// 由 TTS 合成算子产出，供时间对齐与混音算子消费。
/// </summary>
public class DubSegment
{
    /// <summary>要配音的目标语言文本（去空白）。</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>说话人 ID（无分割时为 null）。</summary>
    public string? SpeakerId { get; set; }

    /// <summary>说话人参考音频路径（说话人分割且媒体可用时提供）。</summary>
    public string? ReferenceAudioPath { get; set; }

    /// <summary>目标起始时间（毫秒，来自字幕时间轴）。</summary>
    public double TargetStartMs { get; set; }

    /// <summary>目标结束时间（毫秒）。</summary>
    public double TargetEndMs { get; set; }

    /// <summary>合成产出的 wav 路径（TTS 后）。</summary>
    public string? SynthesizedWavPath { get; set; }

    /// <summary>TTS 原始合成时长（秒，ffprobe 探测）。</summary>
    public double SynthesizedDurationSec { get; set; }

    /// <summary>时间对齐后的时长（秒，atempo 调整后）。</summary>
    public double AlignedDurationSec { get; set; }

    /// <summary>本段最终混音位置偏移（毫秒；仅当相邻段重叠时用于压叠）。</summary>
    public double MixOffsetMs { get; set; }

    /// <summary>最终应用的变速比（atempo；未调整时为 1.0）。</summary>
    public double AlignmentTempo { get; set; } = 1.0;

    /// <summary>情感提示（从字幕文本检测：excited/sad/angry/neutral；当前仅记录，不改变合成）。</summary>
    public string? EmotionHint { get; set; }

    /// <summary>合成是否跳过（TTS 失败时标记，避免混音阶段空引用）。</summary>
    public bool Skipped { get; set; }

    /// <summary>跳过/处理说明（TTS 失败原因等）。</summary>
    public string? Note { get; set; }

    /// <summary>目标时长（秒）。</summary>
[System.Text.Json.Serialization.JsonIgnore]
    public double TargetDurationSec => Math.Max(0, (TargetEndMs - TargetStartMs) / 1000.0);
}
