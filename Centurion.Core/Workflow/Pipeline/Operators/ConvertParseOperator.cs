using System.Text.RegularExpressions;
using Centurion.Abstractions.Pipeline;
using Centurion.Models;
using Centurion.Models.Ass;
using Centurion.Models.Workflow;
using SubtitlesParserV2;
using Centurion.Core.Utils.Parsing;
namespace Centurion.Core.Workflow.Pipeline.Operators;

/// <summary>
/// 转换管道 - 使用 SubtitlesParserV2 解析输入字幕文件，
/// 并将每个字幕条目转换为 Sentence 对象存入 TranscribeSentences。
/// ASS 输入使用项目自有 <see cref="AssSubBuilder"/> 解析，保留样式表与逐行样式。
/// </summary>
public partial class ConvertParseOperator : IPipelineOperator
{
    /// <summary>算子在管道中的显示名称。</summary>
    public string Name => "ConvertParse";

    /// <summary>
    /// 解析输入字幕文件并将每个字幕条目转换为 <see cref="Sentence"/>，写入工作流状态。
    /// </summary>
    /// <param name="context">字幕工作流上下文，提供字幕文件路径。</param>
    /// <param name="cancellationToken">用于取消解析过程的取消标记。</param>
    public async Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken = default)
    {
        var inputPath = context.Config.SubtitleFilePath ?? context.Config.InputFilePath;
        if (string.IsNullOrEmpty(inputPath) || !File.Exists(inputPath))
            throw new FileNotFoundException("Subtitle file not found.", inputPath);

        var isAss = Path.GetExtension(inputPath).Equals(".ass", StringComparison.OrdinalIgnoreCase)
            || Path.GetExtension(inputPath).Equals(".ssa", StringComparison.OrdinalIgnoreCase);

        List<Sentence> sentences;
        if (isAss)
        {
            // ASS：用项目自有解析器，保留样式表与逐行样式引用
            var builder = AssSubBuilder.FromFile(inputPath);
            context.State.Styles = [.. builder.Styles];
            sentences = builder.Lines
                .Where(line => !string.IsNullOrWhiteSpace(line.Text))
                .Select(line => new Sentence
                {
                    Text = line.Text.Trim(),
                    Start = line.GetStart(),
                    End = line.GetEnd(),
                    Style = string.IsNullOrWhiteSpace(line.Style) ? null : line.Style.Trim()
                })
                .ToList();
        }
        else
        {
            await using var stream = File.OpenRead(inputPath);
            var subtitle = SubtitleParser.ParseStream(stream)?.Subtitles;

            if (subtitle == null || subtitle.Count == 0)
                throw new InvalidOperationException("No subtitle items parsed.");

            // 转换为 Sentence 列表
            sentences = subtitle
                .Where(item => item.Lines.Count > 0)
                .Select(item =>
                {
                    var text = string.Join(" ", item.Lines);
                    var words = ParseKaraokeWords(text, item.StartTime, item.EndTime, context.Config.Language);
                    return new Sentence
                    {
                        Text = KaraokeTagRegex().Replace(text, string.Empty).Trim(),
                        Start = item.StartTime,
                        End = item.EndTime,
                        Words = words
                    };
                })
                .ToList();
        }

        if (sentences.Count == 0)
            throw new InvalidOperationException("No subtitle items parsed.");

        // 保留独立基线；转换管道仍使用 TranscribeSentences 作为现有输出槽。
        context.State.SubtitleSentences = sentences.Select(CloneSentence).ToList();
        context.State.TranscribeSentences = sentences;
        context.State.CurrentSentences = sentences;
    }

    private static Sentence CloneSentence(Sentence source)
    {
        return new Sentence
        {
            Text = source.Text,
            CleanedText = source.CleanedText,
            Start = source.Start,
            End = source.End,
            SkipRender = source.SkipRender,
            Style = source.Style,
            Words = source.Words.Select(word => new Word
            {
                Text = word.Text,
                Start = word.Start,
                End = word.End,
                Speaker = word.Speaker,
                PosTag = word.PosTag,
                Status = word.Status
            }).ToList()
        };
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
