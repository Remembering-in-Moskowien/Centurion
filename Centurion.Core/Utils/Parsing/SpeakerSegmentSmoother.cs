using Centurion.Abstractions.Strategy;

namespace Centurion.Core.Utils.Parsing;

/// <summary>
/// 说话人片段后处理平滑器：消除说话人分割输出的逐段交替抖动与过短碎片，
/// 是标准说话人分割（NIST collar / min-duration 合并）的确定性轻量实现。
/// <para>
/// 处理流程：按开始时间排序并保证不重叠 → 合并相邻同说话人片段 →
/// 消除"交替碎片"（夹在两个相同说话人之间的过短异说话人片段并入两侧）→
/// 过短片段并入相邻的较长片段。全部操作仅扩展边界、保持连续不重叠。
/// </para>
/// </summary>
public static class SpeakerSegmentSmoother
{
    /// <summary>
    /// 平滑说话人片段列表。
    /// </summary>
    /// <param name="turns">分割器输出的原始片段（按时间排序、允许轻微交叠）。</param>
    /// <param name="minDurationSeconds">片段最短时长（秒）；短于该值视为碎片参与合并，默认 0.5 秒。</param>
    /// <returns>平滑后的片段列表；空输入返回空列表。</returns>
    public static IReadOnlyList<SpeakerSegment> Smooth(
        IReadOnlyList<SpeakerSegment> turns,
        double minDurationSeconds = 0.5)
    {
        if (turns.Count == 0)
            return [];

        // 1. 按开始时间排序
        var ordered = turns
            .OrderBy(turn => turn.StartSeconds)
            .ToList();

        // 2. 保证不重叠：后一片段起点不得早于前一片段终点
        var nonOverlapping = new List<SpeakerSegment>(ordered.Count);
        foreach (var turn in ordered)
        {
            var start = nonOverlapping.Count > 0
                ? Math.Max(turn.StartSeconds, nonOverlapping[^1].EndSeconds)
                : turn.StartSeconds;
            var end = Math.Max(start, turn.EndSeconds);
            if (end > start)
                nonOverlapping.Add(new SpeakerSegment(start, end, turn.Speaker));
        }

        // 3. 合并相邻同说话人片段
        var merged = new List<SpeakerSegment>(nonOverlapping.Count);
        foreach (var turn in nonOverlapping)
        {
            if (merged.Count > 0
                && string.Equals(merged[^1].Speaker, turn.Speaker, StringComparison.Ordinal))
            {
                var previous = merged[^1];
                merged[^1] = new SpeakerSegment(previous.StartSeconds, turn.EndSeconds, previous.Speaker);
            }
            else
            {
                merged.Add(turn);
            }
        }

        // 4. 消除交替碎片与过短片段（多遍直到收敛：并入/吞并可能产生新的相邻同说话人）
        List<SpeakerSegment> result;
        var pass = merged;
        for (var round = 0; round < 4; round++)
        {
            var next = Pass(pass, minDurationSeconds);
            if (next.SequenceEqual(pass))
            {
                pass = next;
                break;
            }
            pass = next;
        }
        result = pass;

        return result;
    }

    /// <summary>
    /// 单遍处理：合并相邻同说话人（因并入扩展边界可能再产生相邻同说话人，需多遍收敛），
    /// 并消除短于阈值的碎片——夹在相同说话人之间的碎片并入两侧；
    /// 否则并入相邻较长的片段。返回新列表（不修改输入）。
    /// </summary>
    private static List<SpeakerSegment> Pass(List<SpeakerSegment> turns, double minDurationSeconds)
    {
        var result = new List<SpeakerSegment>(turns.Count);
        for (var index = 0; index < turns.Count; index++)
        {
            var current = turns[index];
            var duration = current.EndSeconds - current.StartSeconds;

            // 相邻同说话人（由前一遍并入产生）→ 合并
            if (result.Count > 0
                && string.Equals(result[^1].Speaker, current.Speaker, StringComparison.Ordinal))
            {
                var previous = result[^1];
                result[^1] = new SpeakerSegment(previous.StartSeconds, current.EndSeconds, previous.Speaker);
                continue;
            }

            // 不是碎片 → 保留
            if (duration >= minDurationSeconds)
            {
                result.Add(current);
                continue;
            }

            // 碎片：夹在两个相同说话人之间 → 与两侧合并（吞并）
            var hasPrev = result.Count > 0;
            var hasNext = index + 1 < turns.Count;
            var prevSpeaker = hasPrev ? result[^1].Speaker : null;
            var nextSpeaker = hasNext ? turns[index + 1].Speaker : null;
            if (hasPrev && hasNext
                && string.Equals(prevSpeaker, nextSpeaker, StringComparison.Ordinal))
            {
                var previous = result[^1];
                result[^1] = new SpeakerSegment(previous.StartSeconds, turns[index + 1].EndSeconds, previous.Speaker);
                index++; // 跳过被吞并的下一个片段
                continue;
            }

            // 碎片：并入相邻较长的片段（优先并入前一个；无前一个则并入后一个）
            if (hasPrev && hasNext)
            {
                var previous = result[^1];
                var previousDuration = previous.EndSeconds - previous.StartSeconds;
                if (previousDuration >= turns[index + 1].EndSeconds - turns[index + 1].StartSeconds)
                {
                    result[^1] = new SpeakerSegment(previous.StartSeconds, current.EndSeconds, previous.Speaker);
                }
                else
                {
                    result.Add(new SpeakerSegment(current.StartSeconds, turns[index + 1].EndSeconds, turns[index + 1].Speaker));
                    index++;
                }
            }
            else if (hasPrev)
            {
                var previous = result[^1];
                result[^1] = new SpeakerSegment(previous.StartSeconds, current.EndSeconds, previous.Speaker);
            }
            else if (hasNext)
            {
                result.Add(new SpeakerSegment(current.StartSeconds, turns[index + 1].EndSeconds, turns[index + 1].Speaker));
                index++;
            }
            else
            {
                result.Add(current); // 唯一的碎片片段，无处并入，保留
            }
        }

        return result;
    }
}
