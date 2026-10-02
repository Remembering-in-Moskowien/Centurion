using System.Text.RegularExpressions;
using Centurion.Models;
using Centurion.Models.Ass;
using SubtitlesParserV2;

namespace Centurion.Core.Utils.Parsing;

/// <summary>
/// 字幕文件解析工具：把任意受支持的字幕文件（ASS/SSA 用项目自有
/// <see cref="AssSubBuilder"/> 保留样式表与逐行样式，其余格式用 SubtitlesParserV2 解析，
/// 并抽取卡拉OK \K 词级时间戳）转换为 <see cref="Sentence"/> 列表。
/// 由 convert 命令的转换算子与 combine 命令的来源解析算子共享，保证解析口径一致。
/// </summary>
public static partial class SubtitleFileParser
{
    /// <summary>解析结果：句子列表 + ASS 样式表（非 ASS 输入时样式表为空）。</summary>
    /// <param name="Sentences">解析出的句子（不含来源标记）。</param>
    /// <param name="Styles">ASS/SSA 输入携带的样式表；其余格式为空列表。</param>
    public sealed record ParseResult(List<Sentence> Sentences, List<AssStyle> Styles);

    /// <summary>
    /// 解析字幕文件为句子列表。
    /// </summary>
    /// <param name="path">字幕文件路径（.ass/.ssa 走项目解析器，其余走 SubtitlesParserV2）。</param>
    /// <param name="language">语言代码，用于纯文本字幕的词级时间插值（可为 null）。</param>
    /// <returns>句子列表与（ASS 输入时）样式表。</returns>
    /// <exception cref="FileNotFoundException">文件不存在。</exception>
    /// <exception cref="InvalidOperationException">未解析出任何字幕条目。</exception>
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
