using Catalyst;
using Catalyst.Models;
using Centurion.Core.Abstractions.Strategy;
using Centurion.Core.Models;
using Mosaik.Core;

namespace Centurion.Core.Strategy.SentenceSplit;

/// <summary>
/// 使用 Catalyst 词性标注提取意群，并按字幕长度聚合。
/// </summary>
public class CatalystSplitStrategy : BaseSplitStrategy
{
    private const int ContextWindowSize = 3;
    private static readonly Lock PipelineLock = new();
    private static readonly Dictionary<string, Lazy<Task<Pipeline>>> Pipelines = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<char> BoundaryPunctuation = [.. "。！？；.!?;".ToCharArray()];

    /// <summary>保留接口要求的 Word 入口。</summary>
    public override Task<List<Sentence>> Split(List<Word> words, SplitOptions options)
    {
        if (words is null || words.Count == 0)
            return Task.FromResult<List<Sentence>>([]);

        var parent = new Sentence
        {
            Text = string.Join(" ", words.Select(word => word.Text)),
            Start = words.Min(word => word.Start),
            End = words.Max(word => word.End),
            Words = words
        };
        return Split([parent], options);
    }

    /// <summary>
    /// 按原始句子顺序处理，后续两个句子仅作为 POS 上下文。
    /// </summary>
    public async Task<List<Sentence>> Split(List<Sentence> sentences, SplitOptions? options)
    {
        if (sentences is null || sentences.Count == 0)
            return [];

        options ??= new SplitOptions();
        var input = options.EnableResegmentation ? await ResegmentAsync(sentences, options) : sentences;
        var result = new List<Sentence>();

        for (var i = 0; i < input.Count; i++)
        {
            var parent = input[i];
            if (string.IsNullOrWhiteSpace(parent.Text))
            {
                result.Add(parent);
                continue;
            }

            try
            {
                var windowText = string.Join(" ", input.Skip(i).Take(ContextWindowSize).Select(sentence => sentence.Text ?? string.Empty));
                var currentEnd = Math.Clamp(parent.Text.Length, 0, windowText.Length);
                var document = await ProcessTextAsync(windowText, options);
                var chunks = FilterChunksForCurrentSentence(
                    ExtractChunksFromDocument(document, windowText, options.ChunkGranularity), windowText, currentEnd);

                if (chunks.Count == 0)
                {
                    result.Add(parent);
                    continue;
                }

                var children = AggregateChunksToSentences(chunks, parent, options);
                foreach (var child in children)
                    FillWordTimestamps(parent, child.Sentence, child.StartChar, child.EndChar);
                result.AddRange(children.Select(child => child.Sentence));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Catalyst sentence splitting failed: {ex.Message}");
                result.Add(parent);
            }
        }

        return result;
    }

    /// <summary>使用 Catalyst 句子检测器重建 ASR 边界并映射时间戳。</summary>
    private static async Task<List<Sentence>> ResegmentAsync(List<Sentence> sentences, SplitOptions options)
    {
        try
        {
            var source = new List<SourcePart>();
            var fullText = string.Empty;
            foreach (var sentence in sentences.Where(item => !string.IsNullOrWhiteSpace(item.Text)))
            {
                if (fullText.Length > 0) fullText += " ";
                var start = fullText.Length;
                fullText += sentence.Text;
                source.Add(new SourcePart(start, fullText.Length, sentence));
            }

            if (fullText.Length == 0)
                return sentences;

            var document = await ProcessTextAsync(fullText, options, true);
            var result = new List<Sentence>();
            foreach (var span in document)
            {
                var start = Math.Clamp(span.Begin, 0, fullText.Length);
                var end = Math.Clamp(span.End + 1, start, fullText.Length);
                while (start < end && char.IsWhiteSpace(fullText[start])) start++;
                while (end > start && char.IsWhiteSpace(fullText[end - 1])) end--;
                if (start >= end) continue;

                var parts = source.Where(item => item.EndChar > start && item.StartChar < end).ToList();
                if (parts.Count == 0) continue;
                var startTime = InterpolateTime(parts[0], start);
                var endTime = InterpolateTime(parts[^1], end);
                result.Add(new Sentence
                {
                    Text = fullText[start..end],
                    Start = Math.Min(startTime, endTime),
                    End = Math.Max(startTime, endTime),
                    Words = []
                });
            }
            return result.Count == 0 ? sentences : result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Catalyst resegmentation failed, using original sentences: {ex.Message}");
            return sentences;
        }
    }

    /// <summary>把全局字符位置映射到其所在原始句子的时间轴。</summary>
    private static double InterpolateTime(SourcePart part, int position)
    {
        var length = Math.Max(1, part.EndChar - part.StartChar);
        var ratio = Math.Clamp((double)(position - part.StartChar) / length, 0, 1);
        return part.Sentence.Start + (part.Sentence.End - part.Sentence.Start) * ratio;
    }

    /// <summary>懒加载并复用 Catalyst Pipeline。</summary>
    private static async Task<Document> ProcessTextAsync(string text, SplitOptions options, bool enableSentenceDetector = false)
    {
        var key = $"{NormalizeLanguage(options.Language)}:{enableSentenceDetector}";
        Lazy<Task<Pipeline>> lazy;
        lock (PipelineLock)
        {
            if (!Pipelines.TryGetValue(key, out lazy!))
            {
                lazy = new Lazy<Task<Pipeline>>(
                    () => CreatePipelineAsync(options.ModelCachePath, enableSentenceDetector), true);
                Pipelines[key] = lazy;
            }
        }

        var document = new Document(text, Language.English);
        (await lazy.Value).ProcessSingle(document);
        return document;
    }

    /// <summary>创建带或不带句子检测器的 Catalyst Pipeline。</summary>
    private static async Task<Pipeline> CreatePipelineAsync(string cachePath, bool enableSentenceDetector)
    {
        if (!string.IsNullOrWhiteSpace(cachePath))
            Storage.Current = new DiskStorage(cachePath);
        English.Register();
        return await Pipeline.ForAsync(Language.English, enableSentenceDetector, tagger: true);
    }

    /// <summary>提取意群，并防御 Catalyst 返回的异常字符索引。</summary>
    private static List<Chunk> ExtractChunksFromDocument(Document document, string text, float granularity)
    {
        var tokens = document.SelectMany(span => span.Tokens)
            .Where(token => token.Begin >= 0 && token.End >= token.Begin && token.Begin < text.Length)
            .OrderBy(token => token.Begin).ToList();
        var chunks = new List<Chunk>();
        if (tokens.Count == 0) return chunks;

        var start = Math.Clamp(tokens[0].Begin, 0, text.Length);
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            var tokenEnd = Math.Clamp(token.End + 1, token.Begin + 1, text.Length);
            var next = i + 1 < tokens.Count ? tokens[i + 1] : null;
            if (next is not null && !IsBoundaryAfter(token, next, granularity)) continue;

            var safeStart = Math.Clamp(start, 0, text.Length);
            var safeEnd = Math.Clamp(tokenEnd, safeStart, text.Length);
            var tagged = tokens.Where(item => item.Begin >= safeStart && item.End + 1 <= safeEnd)
                .Select(item => new TaggedToken(item.Value, item.Begin, Math.Clamp(item.End + 1, 0, text.Length), item.POS.ToString()))
                .ToList();
            if (tagged.Count > 0)
            {
                var chunkStart = tagged[0].StartChar;
                var chunkEnd = tagged[^1].EndChar;
                chunks.Add(new Chunk(text[chunkStart..chunkEnd], chunkStart, chunkEnd, tagged));
            }
            if (next is not null) start = Math.Clamp(next.Begin, 0, text.Length);
        }
        return chunks;
    }

    /// <summary>仅保留完整落在当前句子内的 token，杜绝字符级拆词。</summary>
    private static List<Chunk> FilterChunksForCurrentSentence(List<Chunk> chunks, string windowText, int currentEnd)
    {
        var result = new List<Chunk>();
        foreach (var chunk in chunks)
        {
            var tokens = chunk.Tokens.Where(token => token.StartChar >= 0 && token.EndChar <= currentEnd && token.EndChar <= windowText.Length).ToList();
            if (tokens.Count == 0) continue;
            var start = tokens[0].StartChar;
            var end = tokens[^1].EndChar;
            if (start < 0 || start >= end || end > windowText.Length)
            {
                Console.WriteLine($"Catalyst chunk skipped due to invalid token bounds: {start}-{end}");
                continue;
            }
            result.Add(new Chunk(windowText[start..end], start, end, tokens));
        }
        return result;
    }

    private static bool IsBoundaryAfter(IToken token, IToken next, float granularity)
    {
        var punctuation = token.Value.Any(BoundaryPunctuation.Contains);
        var conjunction = next.POS is PartOfSpeech.CCONJ or PartOfSpeech.SCONJ;
        var adposition = next.POS == PartOfSpeech.ADP;
        var adjectiveNoun = token.POS == PartOfSpeech.ADJ && next.POS is PartOfSpeech.NOUN or PartOfSpeech.PROPN;
        var adverbVerb = token.POS == PartOfSpeech.ADV && next.POS is PartOfSpeech.VERB or PartOfSpeech.AUX;
        return punctuation || (granularity >= 0.3f && conjunction) || (granularity >= 0.6f && adposition) || (granularity >= 0.8f && (adjectiveNoun || adverbVerb));
    }

    /// <summary>贪心聚合意群，同时保留聚合结果的原始字符范围。</summary>
    private static List<ChunkSentence> AggregateChunksToSentences(List<Chunk> chunks, Sentence parent, SplitOptions options)
    {
        var output = new List<ChunkSentence>();
        var current = new List<Chunk>();
        var length = 0;
        for (var i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            if (chunk.Text.Length > options.MaxLength)
            {
                output.AddRange(HardSplit(chunk, parent, options.MaxLength));
                current = [];
                length = 0;
                continue;
            }
            current.Add(chunk);
            length += chunk.Text.Length + (current.Count > 1 ? 1 : 0);
            if (length >= options.TargetLength && i + 1 < chunks.Count && IsStrongBoundary(chunks[i + 1]))
            {
                output.Add(BuildSentence(current, parent));
                current = [];
                length = 0;
            }
        }
        if (current.Count > 0) output.Add(BuildSentence(current, parent));
        return output;
    }

    /// <summary>判断意群是否以强标点结束或以连词开始。</summary>
    private static bool IsStrongBoundary(Chunk chunk) => chunk.Text.Any(BoundaryPunctuation.Contains) || chunk.Tokens.FirstOrDefault()?.PosTag is "CCONJ" or "SCONJ";

    /// <summary>按完整 token 边界切分超长意群，单个超长 token 保持完整。</summary>
    private static List<ChunkSentence> HardSplit(Chunk chunk, Sentence parent, int maxLength)
    {
        var output = new List<ChunkSentence>();
        var current = new List<TaggedToken>();
        var length = 0;
        foreach (var token in chunk.Tokens)
        {
            var added = current.Count == 0 ? token.Text.Length : token.Text.Length + 1;
            if (current.Count > 0 && length + added > maxLength)
            {
                output.Add(BuildTokenSentence(current, parent));
                current = [];
                length = 0;
            }
            current.Add(token);
            length += current.Count == 1 ? token.Text.Length : token.Text.Length + 1;
        }
        if (current.Count > 0) output.Add(BuildTokenSentence(current, parent));
        return output;
    }

    /// <summary>构造普通聚合子句并保存其原始字符区间。</summary>
    private static ChunkSentence BuildSentence(List<Chunk> chunks, Sentence parent) => new(
        new Sentence { Text = string.Join(" ", chunks.Select(chunk => chunk.Text)), Start = parent.Start, End = parent.End },
        chunks[0].StartChar, chunks[^1].EndChar);

    /// <summary>构造超长意群的 token 对齐子句。</summary>
    private static ChunkSentence BuildTokenSentence(List<TaggedToken> tokens, Sentence parent) => new(
        new Sentence { Text = string.Join(" ", tokens.Select(token => token.Text)), Start = parent.Start, End = parent.End },
        tokens[0].StartChar, tokens[^1].EndChar);

    /// <summary>按父句字符范围插值时间，并填充子句词级时间戳。</summary>
    private static void FillWordTimestamps(Sentence parent, Sentence child, int startChar, int endChar)
    {
        var duration = Math.Max(0, parent.End - parent.Start);
        var length = Math.Max(1, parent.Text.Length);
        var startRatio = Math.Clamp((double)startChar / length, 0, 1);
        var endRatio = Math.Clamp((double)endChar / length, startRatio, 1);
        child.Start = parent.Start + duration * startRatio;
        child.End = parent.Start + duration * endRatio;

        if (parent.Words is { Count: > 0 })
        {
            child.Words = parent.Words.Where(word => word.End >= child.Start && word.Start <= child.End).Select(word => new Word
            {
                Text = word.Text, Speaker = word.Speaker, PosTag = word.PosTag,
                Start = Math.Max(child.Start, word.Start), End = Math.Min(child.End, word.End)
            }).ToList();
            return;
        }

        var tokens = child.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var tokenDuration = Math.Max(0, child.End - child.Start) / Math.Max(1, tokens.Length);
        child.Words = tokens.Select((token, index) => new Word
        {
            Text = token, Speaker = string.Empty,
            Start = child.Start + index * tokenDuration,
            End = child.Start + (index + 1) * tokenDuration
        }).ToList();
    }

    private static string NormalizeLanguage(string? language) => string.IsNullOrWhiteSpace(language) ? "en" : language.ToLowerInvariant();

    private sealed record Chunk(string Text, int StartChar, int EndChar, List<TaggedToken> Tokens);
    private sealed record TaggedToken(string Text, int StartChar, int EndChar, string PosTag);
    private sealed record ChunkSentence(Sentence Sentence, int StartChar, int EndChar);
    private sealed record SourcePart(int StartChar, int EndChar, Sentence Sentence);
}
