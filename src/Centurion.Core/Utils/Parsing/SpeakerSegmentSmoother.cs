using Centurion.Abstractions.Strategy;

namespace Centurion.Core.Utils.Parsing;

/// <summary>
/// Post-processing smoother for speaker segments: removes per-turn alternating jitter and
/// overly short fragments from diarization output; a deterministic, lightweight implementation
/// of standard diarization (NIST collar / min-duration merging).
/// <para>
/// Pipeline: sort by start time and ensure non-overlapping → merge adjacent same-speaker
/// segments → remove "alternating fragments" (an overly short different-speaker segment wedged
/// between two identical speakers is absorbed into both sides) → absorb overly short segments
/// into a neighboring longer segment. Every operation only expands boundaries and keeps the
/// timeline continuous and non-overlapping.
/// </para>
/// </summary>
public static class SpeakerSegmentSmoother
{
    /// <summary>
    /// Smooths a list of speaker segments.
    /// </summary>
    /// <param name="turns">Raw segments from the diarizer (time-sorted; slight overlap allowed).</param>
    /// <param name="minDurationSeconds">Minimum segment duration in seconds; anything shorter is treated as a fragment and merged. Defaults to 0.5 seconds.</param>
    /// <returns>The smoothed segment list; an empty input yields an empty list.</returns>
    public static IReadOnlyList<SpeakerSegment> Smooth(
        IReadOnlyList<SpeakerSegment> turns,
        double minDurationSeconds = 0.5)
    {
        if (turns.Count == 0)
            return [];

        // 1. Sort by start time
        var ordered = turns
            .OrderBy(turn => turn.StartSeconds)
            .ToList();

        // 2. Ensure non-overlapping: a later segment may not start before the previous one ends
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

        // 3. Merge adjacent same-speaker segments
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

        // 4. Remove alternating fragments and overly short segments (iterate until convergence:
        //    absorbing/merging may create new adjacent same-speaker pairs)
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
    /// Single pass: merge adjacent same-speaker segments (boundary expansion from absorbing may
    /// create new adjacent same-speaker pairs, hence multiple passes until convergence), and drop
    /// fragments shorter than the threshold — a fragment wedged between identical speakers is
    /// absorbed into both sides; otherwise it is absorbed into a neighboring longer segment.
    /// Returns a new list (the input is not modified).
    /// </summary>
    private static List<SpeakerSegment> Pass(List<SpeakerSegment> turns, double minDurationSeconds)
    {
        var result = new List<SpeakerSegment>(turns.Count);
        for (var index = 0; index < turns.Count; index++)
        {
            var current = turns[index];
            var duration = current.EndSeconds - current.StartSeconds;

            // Adjacent same speaker (created by a previous pass) → merge
            if (result.Count > 0
                && string.Equals(result[^1].Speaker, current.Speaker, StringComparison.Ordinal))
            {
                var previous = result[^1];
                result[^1] = new SpeakerSegment(previous.StartSeconds, current.EndSeconds, previous.Speaker);
                continue;
            }

            // Not a fragment → keep
            if (duration >= minDurationSeconds)
            {
                result.Add(current);
                continue;
            }

            // Fragment: wedged between two identical speakers → merge with both sides (absorb)
            var hasPrev = result.Count > 0;
            var hasNext = index + 1 < turns.Count;
            var prevSpeaker = hasPrev ? result[^1].Speaker : null;
            var nextSpeaker = hasNext ? turns[index + 1].Speaker : null;
            if (hasPrev && hasNext
                && string.Equals(prevSpeaker, nextSpeaker, StringComparison.Ordinal))
            {
                var previous = result[^1];
                result[^1] = new SpeakerSegment(previous.StartSeconds, turns[index + 1].EndSeconds, previous.Speaker);
                index++; // skip the absorbed next segment
                continue;
            }

            // Fragment: absorb into a neighboring longer segment (prefer the previous one; if
            // there is none, absorb into the next)
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
                result.Add(current); // the only fragment, nowhere to absorb into, keep it
            }
        }

        return result;
    }
}
