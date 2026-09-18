using Centurion.Models.Workflow;
using Centurion.Abstractions;

namespace Centurion.Abstractions.Strategy;

/// <summary>
/// 说话人分割结果：一个说话人时间片段（单位：秒）。
/// </summary>
/// <param name="StartSeconds">片段开始时间（秒）</param>
/// <param name="EndSeconds">片段结束时间（秒）</param>
/// <param name="Speaker">说话人标签（如 "A"、"B"，或 "SPEAKER_00"）</param>
public sealed record SpeakerSegment(double StartSeconds, double EndSeconds, string Speaker);

/// <summary>
/// 说话人分割策略：给定音频，输出说话人时间片段列表。
/// 具体后端由实现决定（CrispASR 内置方法 / Pyannote + TitaNet 等）。
/// </summary>
public interface IDiarizationStrategy
{
    string StrategyName { get; }

    /// <summary>
    /// 对音频执行说话人分割。
    /// </summary>
    /// <param name="audioPath">预处理后的音频文件路径</param>
    /// <param name="numSpeakers">说话人数（0 = 自动检测）</param>
    /// <param name="segmentModel">分割模型名（后端相关；可为 null 使用后端默认）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <param name="device">推理设备偏好（默认 Auto 自动检测）</param>
    /// <returns>说话人时间片段列表</returns>
    Task<IReadOnlyList<SpeakerSegment>> DiarizeAsync(
        string audioPath,
        int numSpeakers,
        string? segmentModel,
        CancellationToken cancellationToken = default,
        InferenceDevice device = InferenceDevice.Auto);
}
