using System.Text.RegularExpressions;
using Catalyst;
using Centurion.Core.Abstractions;
using Centurion.Core.Exceptions;
using Centurion.Core.Models;
using Centurion.Core.Request;
using Microsoft.Extensions.Options;
using Mosaik.Core;
using Sentence = Centurion.Core.Models.Sentence;

namespace Centurion.Core.Operators;

/// <summary>
/// 基于 Catalyst 的语义分句算子
/// </summary>
public class CatalystSplitOperator : IOperator<SentenceSplitRequest, List<Sentence>>, IAsyncDisposable
{
    private const int TimeGapThresholdMs = 1500;
    private const double MinSentenceDuration = 0.8;
    private const int MaxWordsPerLine = 12;
    private const double MergeGapThreshold = 1.5;
    private const int MinWordsForShort = 3;

    private readonly CatalystSplitOptions _options;
    private readonly IStorage _storage;
    private readonly Dictionary<Language, Pipeline> _pipelines = new();
    private bool _disposed;

    public CatalystSplitOperator(IOptions<CatalystSplitOptions> options) : this(options.Value) { }

    public CatalystSplitOperator(CatalystSplitOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _storage = new DiskStorage(_options.ModelCachePath);
        Storage.Current = _storage;
    }

    public async Task EnsureTargetAvailableAsync()
    {
        if (!Directory.Exists(_options.ModelCachePath))
            throw new DirectoryNotFoundException($"模型缓存目录不存在: {_options.ModelCachePath}");

        try
        {
            var defaultLang = GetLanguage(_options.DefaultLanguage);
            var pipeline = await GetPipelineAsync(defaultLang);
            if (pipeline == null)
                throw new InvalidOperationException($"无法加载 {defaultLang} 语言的 Pipeline");
        }
        catch (Exception ex)
        {
            throw new CatalystException($"Catalyst 初始化失败，请检查目录 {_options.ModelCachePath} 中的模型文件", ex);
        }
    }

    public async Task<List<Sentence>> ProcessAsync(
        OperatorsRequest<SentenceSplitRequest> request,
        CancellationToken cancellationToken = default)
    {
        await EnsureTargetAvailableAsync();
        var payload = request.Payload;
        return await SplitAsync(payload, cancellationToken);
    }

    private async Task<List<Sentence>> SplitAsync(SentenceSplitRequest payload, CancellationToken cancellationToken)
    {
        var words = payload.Sentence.Words;
        if (words == null || words.Count == 0)
            return [];

        cancellationToken.ThrowIfCancellationRequested();

        var normWords = words.Select(w => new Word
        {
            Text = w.Text.Trim(),
            Start = w.Start,
            End = w.End,
            Speaker = w.Speaker
        }).ToList();

        var timeSegments = SplitByTimeGap(normWords, TimeGapThresholdMs);
        var allSentences = new List<Sentence>();

        var language = GetLanguage(payload.Language ?? _options.DefaultLanguage);
        var pipeline = GetPipelineSync(language);

        foreach (var seg in timeSegments)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fullText = string.Join(" ", seg.Select(w => w.Text));
            if (string.IsNullOrWhiteSpace(fullText))
                continue;

            string[] sentenceTexts;
            try
            {
                var doc = new Document(fullText, language);
                pipeline.ProcessSingle(doc);

                var sentenceList = new List<string>();
                if (doc.Spans != null)
                    foreach (var span in doc.Spans)
                        sentenceList.Add(span.Value);

                sentenceTexts = sentenceList.Count > 0 ? [.. sentenceList] : [fullText];
            }
            catch
            {
                sentenceTexts = [fullText];
            }

            var wordPositions = BuildWordPositions(seg, fullText);

            foreach (var sentText in sentenceTexts)
            {
                if (string.IsNullOrWhiteSpace(sentText))
                    continue;

                var wordsInSent = ExtractWordsForSentence(sentText, wordPositions, fullText);
                if (wordsInSent.Count == 0)
                    continue;

                var capGroups = SplitByCapitalization([wordsInSent]);
                foreach (var group in capGroups)
                {
                    if (group.Count == 0) continue;
                    var subText = string.Join(" ", group.Select(w => w.Text));
                    subText = NormalizePunctuation(subText);
                    allSentences.Add(new Sentence
                    {
                        Text = subText,
                        Start = group.First().Start,
                        End = group.Last().End,
                        Words = group
                    });
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        allSentences = [.. allSentences.OrderBy(s => s.Start)];
        allSentences = MergeShortSentences(allSentences);

        var maxLen = payload.MaxLength ?? _options.DefaultMaxLength ?? 80;
        var targetLen = payload.TargetLength ?? _options.DefaultTargetLength ?? 50;
        allSentences = SplitLongSentences(allSentences, maxLen, targetLen);
        allSentences = FixPunctuationIssues(allSentences);

        return allSentences;
    }

    // ---------- 规则辅助方法 ----------
    private List<List<Word>> SplitByTimeGap(List<Word> words, int gapMs)
    {
        if (words == null || words.Count == 0) return [];
        words = [.. words.OrderBy(w => w.Start)];
        var segments = new List<List<Word>>();
        var current = new List<Word> { words[0] };

        for (var i = 1; i < words.Count; i++)
        {
            var gap = words[i].Start - words[i - 1].End;
            if (gap > gapMs)
            {
                segments.Add(current);
                current = [words[i]];
            }
            else
            {
                current.Add(words[i]);
            }
        }

        if (current.Any())
            segments.Add(current);

        return segments;
    }

    private List<List<Word>> SplitByCapitalization(List<List<Word>> groups)
    {
        var result = new List<List<Word>>();
        foreach (var group in groups)
        {
            if (group.Count < 2)
            {
                result.Add(group);
                continue;
            }

            var subGroups = new List<List<Word>>();
            var current = new List<Word> { group[0] };
            for (var i = 1; i < group.Count; i++)
            {
                var prev = group[i - 1];
                var curr = group[i];
                var prevText = prev.Text.Trim();
                var currText = curr.Text.Trim();

                var shouldSplit = currText.Length > 0 &&
                                  char.IsUpper(currText[0]) &&
                                  currText.Length > 1 &&
                                  !(prevText.EndsWith(".") || prevText.EndsWith("!") || prevText.EndsWith("?")) &&
                                  !IsAbbreviation(prevText) &&
                                  !IsAllUpper(currText);

                if (shouldSplit)
                {
                    subGroups.Add(current);
                    current = [curr];
                }
                else
                {
                    current.Add(curr);
                }
            }

            if (current.Any())
                subGroups.Add(current);

            result.AddRange(subGroups);
        }

        return result;
    }

    private bool IsAbbreviation(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        string[] common = ["Mr.", "Mrs.", "Ms.", "Dr.", "e.g.", "i.e.", "vs.", "etc.", "U.S."];
        if (common.Contains(text)) return true;
        return Regex.IsMatch(text, @"^[A-Z](?:\.[A-Z])+\.?$");
    }

    private bool IsAllUpper(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        return text.All(c => !char.IsLetter(c) || char.IsUpper(c));
    }

    private string NormalizePunctuation(string text)
        => Regex.Replace(text, @"\s+([.,!?;:])", "$1");

    private List<Sentence> MergeShortSentences(List<Sentence> sentences)
    {
        if (sentences == null || sentences.Count < 2) return sentences ?? [];
        var merged = new List<Sentence>();
        var i = 0;
        while (i < sentences.Count)
        {
            var cur = sentences[i];
            if (i + 1 >= sentences.Count)
            {
                merged.Add(cur);
                break;
            }

            var nxt = sentences[i + 1];
            var curDuration = cur.End - cur.Start;
            var curWordCount = cur.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            var gap = nxt.Start - cur.End;

            var isShort = (curDuration < MinSentenceDuration && curWordCount <= MinWordsForShort) || curWordCount == 1;

            if (isShort && gap < MergeGapThreshold)
            {
                var totalWords = curWordCount + nxt.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                if (totalWords <= MaxWordsPerLine)
                {
                    var mergedText = cur.Text + " " + nxt.Text;
                    var mergedWords = cur.Words.Concat(nxt.Words).ToList();
                    merged.Add(new Sentence
                    {
                        Text = mergedText,
                        Start = cur.Start,
                        End = nxt.End,
                        Words = mergedWords
                    });
                    i += 2;
                    continue;
                }
            }

            merged.Add(cur);
            i++;
        }

        return merged;
    }

    private List<Sentence> SplitLongSentences(List<Sentence> sentences, int maxLength, int targetLength)
    {
        var result = new List<Sentence>();
        foreach (var sent in sentences)
        {
            if (sent.Text.Length <= maxLength)
            {
                result.Add(sent);
                continue;
            }

            var words = sent.Words;
            if (words == null || words.Count == 0)
            {
                result.Add(sent);
                continue;
            }

            var totalChars = sent.Text.Length;
            if (totalChars == 0)
            {
                result.Add(sent);
                continue;
            }

            var targetWordsPerChunk = Math.Max(1, (int)((double)targetLength / totalChars * words.Count));
            if (targetWordsPerChunk < 2) targetWordsPerChunk = 2;

            var chunks = new List<List<Word>>();
            var currentChunk = new List<Word>();
            var currentCharCount = 0;

            foreach (var w in words)
            {
                var wordLen = w.Text.Length + 1;
                if (currentChunk.Count == 0)
                {
                    currentChunk.Add(w);
                    currentCharCount = wordLen;
                    continue;
                }

                var isPunctuation = w.Text.Length == 1 && ".,!?;:".Contains(w.Text);
                if ((currentCharCount + wordLen > targetLength || currentChunk.Count >= targetWordsPerChunk) &&
                    !isPunctuation)
                {
                    chunks.Add(currentChunk);
                    currentChunk = [w];
                    currentCharCount = wordLen;
                }
                else
                {
                    currentChunk.Add(w);
                    currentCharCount += wordLen;
                }
            }

            if (currentChunk.Any())
                chunks.Add(currentChunk);

            if (chunks.Count <= 1)
            {
                result.Add(sent);
                continue;
            }

            foreach (var chunk in chunks)
            {
                var first = chunk.First();
                var last = chunk.Last();
                var text = string.Join(" ", chunk.Select(w => w.Text));
                text = NormalizePunctuation(text);
                result.Add(new Sentence
                {
                    Text = text,
                    Start = first.Start,
                    End = last.End,
                    Words = chunk
                });
            }
        }

        return result;
    }

    private List<Sentence> FixPunctuationIssues(List<Sentence> sentences)
    {
        if (sentences == null || sentences.Count < 2) return sentences ?? [];
        var fixedList = new List<Sentence>();
        for (var i = 0; i < sentences.Count; i++)
        {
            var current = sentences[i];
            if (current.Words != null && current.Words.Count > 0)
            {
                var firstWord = current.Words[0];
                var trimmedText = firstWord.Text.Trim();
                var isPunctuationOnly = trimmedText.Length == 1 && ".,!?;:".Contains(trimmedText);
                if (isPunctuationOnly && i > 0)
                {
                    var prev = fixedList.Last();
                    prev.Words.Add(firstWord);
                    prev.Text = string.Join(" ", prev.Words.Select(w => w.Text));
                    prev.Text = NormalizePunctuation(prev.Text);
                    if (firstWord.End > prev.End)
                        prev.End = firstWord.End;

                    current.Words.RemoveAt(0);
                    if (current.Words.Count > 0)
                    {
                        current.Start = current.Words[0].Start;
                        current.Text = string.Join(" ", current.Words.Select(w => w.Text));
                        current.Text = NormalizePunctuation(current.Text);
                        fixedList.Add(current);
                    }

                    continue;
                }
            }

            fixedList.Add(current);
        }

        return fixedList;
    }

    // ---------- 辅助：字符位置映射 ----------
    private List<(int StartChar, int EndChar, Word Word)> BuildWordPositions(List<Word> words, string fullText)
    {
        var positions = new List<(int, int, Word)>();
        var pos = 0;
        foreach (var w in words)
        {
            var start = pos;
            var end = start + w.Text.Length;
            positions.Add((start, end, w));
            pos = end + 1;
        }

        return positions;
    }

    private List<Word> ExtractWordsForSentence(string sentence,
        List<(int Start, int End, Word Word)> wordPositions, string fullText)
    {
        var idx = fullText.IndexOf(sentence, StringComparison.Ordinal);
        if (idx == -1)
        {
            var trimmed = sentence.Trim();
            idx = fullText.IndexOf(trimmed, StringComparison.Ordinal);
            if (idx != -1)
            {
                var endIdx = idx + trimmed.Length;
                sentence = fullText.Substring(idx, endIdx - idx);
            }
            else
            {
                return [.. wordPositions.Select(p => p.Word)];
            }
        }

        var sentStart = idx;
        var sentEnd = idx + sentence.Length;

        var result = new List<Word>();
        foreach (var (s, e, w) in wordPositions)
            if (s < sentEnd && e > sentStart)
                result.Add(w);
        return result;
    }

    // ---------- Catalyst 模型管理 ----------
    private async Task<Pipeline> GetPipelineAsync(Language language)
    {
        if (!_pipelines.TryGetValue(language, out var pipeline))
        {
            RegisterLanguage(language);
            pipeline = await Pipeline.TokenizerForAsync(language);
            _pipelines[language] = pipeline;
        }

        return pipeline;
    }

    private Pipeline GetPipelineSync(Language language)
    {
        if (!_pipelines.TryGetValue(language, out var pipeline))
        {
            RegisterLanguage(language);
            pipeline = Pipeline.TokenizerForAsync(language).GetAwaiter().GetResult();
            _pipelines[language] = pipeline;
        }

        return pipeline;
    }

    private void RegisterLanguage(Language language)
    {
        switch (language)
        {
            case Language.English:
                Catalyst.Models.English.Register();
                break;
            default:
                Catalyst.Models.English.Register();
                break;
        }
    }

    private Language GetLanguage(string languageCode)
        => languageCode?.ToLower() switch
        {
            "en" => Language.English,
            "zh" => Language.Chinese,
            "es" => Language.Spanish,
            "fr" => Language.French,
            "de" => Language.German,
            _ => Language.English
        };

    // ---------- 资源释放 ----------
    public void Dispose() => DisposeAsync().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _pipelines.Clear();
        if (_storage is IDisposable disposableStorage)
            disposableStorage.Dispose();

        await Task.CompletedTask;
    }
}

public class CatalystSplitOptions
{
    public string ModelCachePath { get; set; } = "./models/catalyst";
    public string DefaultLanguage { get; set; } = "en";
    public int? DefaultMaxLength { get; set; } = 80;
    public int? DefaultTargetLength { get; set; } = 50;
}