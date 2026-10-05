using Centurion.Abstractions.Strategy;
using Centurion.Models.Transcript;

namespace Centurion.Core.Utils.Parsing;

/// <summary>
/// Parses the JSON output of CrispASR --diarize-speakers (-ojf format) and extracts speaker time segments.
/// Speaker info lives in the speaker field of each entry in the transcription array (e.g. "(speaker 0)");
/// timing comes from that entry's millisecond offsets.
/// </summary>
public static class DiarizationJsonParser
{
    /// <summary>
    /// Parse the CrispASR --diarize-speakers output and extract the time segments of each speaker.
    /// Entries lacking a speaker tag are ignored.
    /// </summary>
    /// <param name="json">The JSON string in CrispASR -ojf format.</param>
    /// <returns>The parsed speaker segment list (times in seconds).</returns>
    /// <exception cref="InvalidOperationException">Thrown when the output lacks the transcription array.</exception>
    public static IReadOnlyList<SpeakerSegment> Parse(string json)
    {
        var root = JsonParser.Deserialize<CrispAsrTranscriptJson>(json);

        if (root.Transcription is null)
            throw new InvalidOperationException("Missing 'transcription' array in diarized JSON output.");

        var result = new List<SpeakerSegment>();
        foreach (var item in root.Transcription)
        {
            var speaker = ExtractSpeaker(item.Speaker);
            if (speaker is null)
                continue;

            result.Add(new SpeakerSegment(
                item.Offsets.From / 1000.0,
                item.Offsets.To / 1000.0,
                speaker));
        }

        return result;
    }

    /// <summary>
    /// Normalize a speaker label: "(speaker 0)" -> "speaker 0"; non-parenthesized forms keep the original text with surrounding whitespace trimmed.
    /// </summary>
    /// <param name="raw">The raw speaker field value from CrispASR output.</param>
    /// <returns>The normalized label; null when blank or empty.</returns>
    private static string? ExtractSpeaker(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var trimmed = raw.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '(' && trimmed[^1] == ')')
            return trimmed[1..^1].Trim();

        return trimmed;
    }
}
