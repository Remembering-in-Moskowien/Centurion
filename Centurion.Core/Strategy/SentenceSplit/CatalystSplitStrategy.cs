using Catalyst;
using Catalyst.Models;
using Centurion.Core.Abstractions.Strategy;
using Centurion.Core.Models;
using Mosaik.Core;
using System.Text.RegularExpressions;

namespace Centurion.Core.Strategy.SentenceSplit;

/// <summary>
/// 使用 Catalyst 词性标注结果提取意群，并按字幕长度聚合。
/// </summary>
public class CatalystSplitStrategy : BaseSplitStrategy
{
    private const int ContextWindowSize = 3;
    private static readonly object PipelineLock = new();
    private static readonly Dictionary<string, Lazy<Task<Pipeline>>> Pipelines = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);
    private static readonly HashSet<char> BoundaryPunctuation = new("。！？；.!?;".ToCharArray());

    /// <summary>
    /// 保留接口要求的 Word 入口；单个 Word 列表被视为一个父句。
    /// </summary>
    public override async Task<List<Sentence>> Split(List<Word> words, SplitOptions options)
    {
        if (words is null || words.Count == 0)
            return [];

        var parent = new Sentence
        {
            Text = string.Join(" ", words.Select(word => word.Text)),
            Start = words.Min(word => word.Start),
            End = words.Max(word => word.End),
            Words = words
        };
        return await Split([parent], options);
    }

    /// <summary>
    /// 按原始 Sentence 顺序处理。窗口中的后续句子只用于 POS 上下文，不参与当前输出。
    /// </summary>
    public async Task<List<Sentence>> Split(List<Sentence> sentences, SplitOptions options)
    {
        if (sentences is null || sentences.Count == 0)
            return [];

        options ??= new SplitOptions();
        var result = new List<Sentence>();

        for (var i = 0; i < sentences.Count; i++)
        {
            var parent = sentences[i];
            if (string.IsNullOrWhiteSpace(parent.Text))
            {
                result.Add(parent);
                continue;
            }

            try
            {
                var windowTexts = sentences
                    .Skip(i)
                    .Take(ContextWindowSize)
                    .Select(sentence => sentence.Text ?? string.Empty)
                    .ToList();
                var windowText = string.Join(" ", windowTexts);
                var currentEnd = parent.Text.Length;
                var document = await ProcessTextAsync(windowText, options);
                var chunks = ExtractChunksFromDocument(document, windowText, options.ChunkGranularity)
                    .Where(chunk => chunk.StartChar < currentEnd && chunk.EndChar > 0)
                    .Select(chunk => chunk with
                    {
                        Text = windowText[Math.Max(0, chunk.StartChar)..Math.Min(currentEnd, chunk.EndChar)].Trim(),
                        StartChar = Math.Max(0, chunk.StartChar),
                        EndChar = Math.Min(currentEnd, chunk.EndChar)
                    })
                    .Where(chunk => chunk.EndChar > chunk.StartChar)
                    .ToList();

                if (chunks.Count == 0)
                {
                    result.Add(parent);
                    continue;
                }

                var children = AggregateChunksToSentences(chunks, parent, options);
                var searchOffset = 0;
                foreach (var child in children)
                {
                    var startChar = parent.Text.IndexOf(child.Text, searchOffset, StringComparison.Ordinal);
                    if (startChar < 0)
                        startChar = searchOffset;
                    var endChar = Math.Min(parent.Text.Length, startChar + child.Text.Length);
                    FillWordTimestamps(parent, child, startChar, endChar);
                    searchOffset = endChar;
                }
                result.AddRange(children.Count == 0 ? [parent] : children);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Catalyst sentence splitting failed: {ex.Message}");
                result.Add(parent);
            }
        }

        return result;
    }

    private static async Task<Document> ProcessTextAsync(string text, SplitOptions options)
    {
        var language = NormalizeLanguage(options.Language);
        Lazy<Task<Pipeline>> lazyPipeline;
        lock (PipelineLock)
        {
            if (!Pipelines.TryGetValue(language, out lazyPipeline!))
            {
                lazyPipeline = new Lazy<Task<Pipeline>>(() => CreatePipelineAsync(language, options.ModelCachePath), true);
                Pipelines[language] = lazyPipeline;
            }
        }

        var pipeline = await lazyPipeline.Value;
        var document = new Document(text, Language.English);
        pipeline.ProcessSingle(document);
        return document;
    }

    private static async Task<Pipeline> CreatePipelineAsync(string language, string cachePath)
    {
        if (!string.IsNullOrWhiteSpace(cachePath))
            Storage.Current = new DiskStorage(cachePath);

        // 英文模型包提供稳定的 tokenizer/tagger；其他语言在这里安全降级为同一模型。
        English.Register();
        return await Pipeline.ForAsync(Language.English, sentenceDetector: false, tagger: true);
    }

    private static List<Chunk> ExtractChunksFromDocument(Document document, string text, float granularity)
    {
        var tokens = document.SelectMany(span => span.Tokens)
            .OrderBy(token => token.Begin)
            .ToList();
        if (tokens.Count == 0)
            return [];

        var chunks = new List<Chunk>();
        var start = tokens[0].Begin;
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            var next = i + 1 < tokens.Count ? tokens[i + 1] : null;
            var shouldSplit = next is null || IsBoundaryAfter(token, next, granularity);
            if (!shouldSplit)
                continue;

            var end = token.End + 1;
            chunks.Add(new Chunk(text[start..end].Trim(), start, end,
                tokens.Where(item => item.Begin >= start && item.End < end)
                    .Select(item => new TaggedToken(item.Value, item.Begin, item.End + 1, item.POS.ToString()))
                    .ToList()));
            if (next is not null)
                start = next.Begin;
        }
        return chunks.Where(chunk => !string.IsNullOrWhiteSpace(chunk.Text)).ToList();
    }

    private static bool IsBoundaryAfter(IToken token, IToken next, float granularity)
    {
        var punctuation = token.Value.Any(BoundaryPunctuation.Contains);
        var conjunction = next.POS is PartOfSpeech.CCONJ or PartOfSpeech.SCONJ;
        var adposition = next.POS == PartOfSpeech.ADP;
        var adjectiveNoun = token.POS == PartOfSpeech.ADJ && next.POS is PartOfSpeech.NOUN or PartOfSpeech.PROPN;
        var adverbVerb = token.POS == PartOfSpeech.ADV && next.POS is PartOfSpeech.VERB or PartOfSpeech.AUX;
        return punctuation || (granularity >= 0.3f && conjunction) ||
               (granularity >= 0.6f && adposition) ||
               (granularity >= 0.8f && (adjectiveNoun || adverbVerb));
    }

    private static List<Sentence> AggregateChunksToSentences(List<Chunk> chunks, Sentence parent, SplitOptions options)
    {
        var output = new List<Sentence>();
        var current = new List<Chunk>();
        var length = 0;

        foreach (var chunk in chunks)
        {
            if (chunk.Text.Length > options.MaxLength)
            {
                output.AddRange(HardSplit(chunk, parent, options.MaxLength));
                current.Clear();
                length = 0;
                continue;
            }

            current.Add(chunk);
            length += chunk.Text.Length + (current.Count > 1 ? 1 : 0);
            var nextIsStrong = chunks.IndexOf(chunk) + 1 < chunks.Count && IsStrongBoundary(chunks[chunks.IndexOf(chunk) + 1]);
            if (length >= options.TargetLength && nextIsStrong)
            {
                output.Add(BuildSentence(current, parent));
                current.Clear();
                length = 0;
            }
        }
        if (current.Count > 0)
            output.Add(BuildSentence(current, parent));
        return output;
    }

    private static bool IsStrongBoundary(Chunk chunk) =>
        chunk.Text.Any(BoundaryPunctuation.Contains) ||
        chunk.Tokens.FirstOrDefault()?.PosTag is "CCONJ" or "SCONJ";

    private static List<Sentence> HardSplit(Chunk chunk, Sentence parent, int maxLength)
    {
        var pieces = new List<Sentence>();
        var text = chunk.Text;
        for (var offset = 0; offset < text.Length; offset += maxLength)
        {
            var length = Math.Min(maxLength, text.Length - offset);
            var piece = text.Substring(offset, length);
            pieces.Add(new Sentence { Text = piece, Start = parent.Start, End = parent.End });
        }
        return pieces;
    }

    private static Sentence BuildSentence(List<Chunk> chunks, Sentence parent)
    {
        return new Sentence
        {
            Text = Whitespace.Replace(string.Join(" ", chunks.Select(chunk => chunk.Text)), " ").Trim(),
            Start = parent.Start,
            End = parent.End
        };
    }

    private static void FillWordTimestamps(Sentence parent, Sentence child, int startChar, int endChar)
    {
        var parentDuration = Math.Max(0, parent.End - parent.Start);
        var parentLength = Math.Max(1, parent.Text.Length);
        var startRatio = Math.Clamp((double)startChar / parentLength, 0, 1);
        var endRatio = Math.Clamp((double)endChar / parentLength, startRatio, 1);
        child.Start = parent.Start + parentDuration * startRatio;
        child.End = parent.Start + parentDuration * endRatio;

        if (parent.Words.Count > 0)
        {
            child.Words = parent.Words
                .Where(word => word.End >= child.Start && word.Start <= child.End)
                .Select(word => new Word { Text = word.Text, Speaker = word.Speaker, PosTag = word.PosTag,
                    Start = Math.Max(child.Start, word.Start), End = Math.Min(child.End, word.End) })
                .ToList();
            return;
        }

        var tokens = child.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var duration = Math.Max(0, child.End - child.Start) / Math.Max(1, tokens.Length);
        child.Words = tokens.Select((token, index) => new Word
        {
            Text = token,
            Speaker = string.Empty,
            Start = child.Start + index * duration,
            End = child.Start + (index + 1) * duration
        }).ToList();
    }

    private static string NormalizeLanguage(string? language) => string.IsNullOrWhiteSpace(language) ? "en" : language.ToLowerInvariant();

    private sealed record Chunk(string Text, int StartChar, int EndChar, List<TaggedToken> Tokens);
    private sealed record TaggedToken(string Text, int StartChar, int EndChar, string PosTag);
}