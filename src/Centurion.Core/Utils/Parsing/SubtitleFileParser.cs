using System.Text.RegularExpressions;
using Centurion.Models;
using Centurion.Models.Ass;
using SubtitlesParserV2;

namespace Centurion.Core.Utils.Parsing;

/// <summary>
/// Subtitle file parsing utility: converts any supported subtitle file into a list of
/// <see cref="Sentence"/>. ASS/SSA uses the project's own <see cref="AssSubBuilder"/> to preserve
/// the style sheet and per-line styles; other formats are parsed with SubtitlesParserV2, and
/// karaoke \K word-level timestamps are extracted. Shared by the convert command's conversion
/// operator and the combine command's source-parsing operator so that parsing stays consistent.
/// </summary>
public static partial class SubtitleFileParser
{
    /// <summary>Parse result: a sentence list plus the ASS style sheet (empty for non-ASS input).</summary>
    /// <param name="Sentences">The parsed sentences (without source markers).</param>
    /// <param name="Styles">The style sheet carried by ASS/SSA input; an empty list for other formats.</param>
    public sealed record ParseResult(List<Sentence> Sentences, List<AssStyle> Styles);

    /// <summary>
    /// Parses a subtitle file into a list of sentences.
    /// </summary>
    /// <param name="path">Path to the subtitle file (.ass/.ssa go through the project parser; other formats go through SubtitlesParserV2).</param>
    /// <param name="language">Language code used for word-level time interpolation of plain-text subtitles (may be null).</param>
    /// <returns>The sentence list and (for ASS input) the style sheet.</returns>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="InvalidOperationException">No subtitle items could be parsed.</exception>
    public static ParseResult Parse(string path, string? language)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            throw new FileNotFoundException("Subtitle file not found.", path);

        var extension = Path.GetExtension(path);
        var isAss = extension.Equals(".ass", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".ssa", StringComparison.OrdinalIgnoreCase);

        if (isAss)
        {
            var builder = AssSubBuilder.FromFile(path);
            var sentences = builder.Lines
                .Where(line => !string.IsNullOrWhiteSpace(line.Text))
                .Select(line => new Sentence
                {
                    Text = line.Text.Trim(),
                    Start = line.GetStart(),
                    End = line.GetEnd(),
                    Style = string.IsNullOrWhiteSpace(line.Style) ? null : line.Style.Trim()
                })
                .ToList();

            return ThrowIfEmpty(new ParseResult(sentences, [.. builder.Styles]), path);
        }

        using var stream = File.OpenRead(path);
        var subtitle = SubtitleParser.ParseStream(stream)?.Subtitles;

        if (subtitle == null || subtitle.Count == 0)
            throw new InvalidOperationException($"No subtitle items parsed from '{path}'.");

        var items = subtitle
            .Where(item => item.Lines.Count > 0)
            .Select(item =>
            {
                var text = string.Join(" ", item.Lines);
                var words = ParseKaraokeWords(text, item.StartTime, item.EndTime, language);
                return new Sentence
                {
                    Text = KaraokeTagRegex().Replace(text, string.Empty).Trim(),
                    Start = item.StartTime,
                    End = item.EndTime,
                    Words = words
                };
            })
            .ToList();

        return ThrowIfEmpty(new ParseResult(items, []), path);
    }

    private static ParseResult ThrowIfEmpty(ParseResult result, string path)
    {
        if (result.Sentences.Count == 0)
            throw new InvalidOperationException($"No subtitle items parsed from '{path}'.");
        return result;
    }

    private static List<Word> ParseKaraokeWords(string text, double sentenceStart, double sentenceEnd, string? language)
    {
        var matches = KaraokeWordRegex().Matches(text);
        if (matches.Count == 0)
            return SubtitleWordSplitter.SplitPlainWords(text, sentenceStart, sentenceEnd, language);

        var words = new List<Word>(matches.Count);
        var cursor = sentenceStart;
        foreach (Match match in matches)
        {
            if (!double.TryParse(match.Groups[1].Value, out var centiseconds))
                continue;

            var wordText = match.Groups[2].Value.Trim();
            if (wordText.Length == 0)
                continue;

            var start = cursor;
            var end = Math.Min(sentenceEnd, start + centiseconds * 10);
            words.Add(new Word
            {
                Text = wordText,
                Start = start,
                End = end,
                Speaker = "UNKNOWN",
                Status = MappingStatus.Matched
            });
            cursor = end;
        }

        return words;
    }

    [GeneratedRegex(@"\{\\[Kk](\d+)\}([^{}]*)")]
    private static partial Regex KaraokeWordRegex();

    [GeneratedRegex(@"\{\\[Kk]\d+\}")]
    private static partial Regex KaraokeTagRegex();
}
