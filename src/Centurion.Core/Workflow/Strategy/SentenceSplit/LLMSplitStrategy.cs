using System.Text.RegularExpressions;
using Centurion.Abstractions.Strategy;
using Centurion.Models;
using Centurion.Models.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Centurion.Abstractions.Utils;
using Centurion.Core.Utils.Parsing;
namespace Centurion.Core.Workflow.Strategy.SentenceSplit;

/// <summary>
/// LLM-based splitting strategy: the input is first stripped of punctuation and lowercased, then the LLM restores punctuation and splits.
/// With smart fallback: if the LLM output is abnormal, fuzzy matching aligns sentence boundaries back to the original word sequence.
/// </summary>
public class LLMSplitStrategy : BaseSplitStrategy
{
    /// <summary>Display name of the LLM split strategy.</summary>
    public override string StrategyName => "llm";

    private readonly IChatClient _chatClient;
    private readonly ILogger<LLMSplitStrategy>? _logger;
    private const double MatchThreshold = 0.5;

    // Regex to extract the JSON array (supports a bare array or a Markdown code block)
    private static readonly Regex JsonArrayRegex = new(@"\[\s*""(?:[^""\\]|\\.)*""\s*(?:,\s*""(?:[^""\\]|\\.)*""\s*)*\]", RegexOptions.Compiled);

    /// <summary>Creates an LLM-based splitting strategy instance.</summary>
    /// <param name="chatClient">The chat client used to call the large language model.</param>
    /// <param name="logger">Optional logger; when null, nothing is logged.</param>
    public LLMSplitStrategy(IChatClient chatClient, ILogger<LLMSplitStrategy>? logger = null)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _logger = logger;
    }

    /// <summary>
    /// Calls the LLM to restore punctuation and split the punctuation-stripped, lowercased word stream;
    /// on abnormal LLM output, it tries count mapping, then fuzzy alignment, and finally falls back to rule splitting.
    /// </summary>
    /// <param name="words">The word stream to split.</param>
    /// <param name="options">Configuration options such as split length and language.</param>
    /// <returns>The list of sentences after splitting.</returns>
    public override async Task<List<Sentence>> Split(List<Word> words, SplitOptions options)
    {
        if (words.Count == 0)
            return [];

        // ----- Preprocessing: strip punctuation and casing -----
        var cleanWords = words.Select(w => new string(w.Text.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant())
                              .Where(w => !string.IsNullOrEmpty(w))
                              .ToList();
        var cleanText = LanguageSupport.JoinMixed(cleanWords);
        if (string.IsNullOrWhiteSpace(cleanText))
            return [];

        var prompt = BuildSplitPrompt(cleanText, options);
        _logger?.LogDebug("Prompt built, length: {Length}", prompt.Length);

        try
        {
            _logger?.LogDebug("Calling _chatClient.GetResponseAsync...");
            var response = await _chatClient.GetResponseAsync(prompt);
            _logger?.LogDebug("GetResponseAsync completed.");

            if (response == null || response.Messages.Count == 0)
                throw new InvalidOperationException("LLM returned null or empty response.");

            var messageText = response.Messages[0].Text;
            if (string.IsNullOrEmpty(messageText))
                throw new InvalidOperationException("LLM response message text is empty.");

            _logger?.LogDebug("Raw LLM response: {Response}", messageText);

            // Try to parse the JSON (strip extra surrounding text if present)
            var splitResult = ParseResponse(messageText);
            return BuildSentencesWithFallback(words, splitResult, options);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "LLM request failed. Falling back to rule-based splitting.");
            return FallbackSplit(words, options);
        }
    }

    // ---------- Prompt design: force pure JSON output ----------
    private string BuildSplitPrompt(string cleanText, SplitOptions options)
    {
        var lang = options.Language?.ToLowerInvariant() ?? "en";

        // Chinese and Japanese are written continuously with shared punctuation; Korean uses English punctuation (. , ? !), so the English prompt branch suffices
        if (lang == "zh" || lang == "zh-cn" || lang == "zh-tw" || lang == "ja" || lang == "ja-jp")
        {
            return $@"
你是一位专业字幕分句与文本规范化专家。输入文本为**无标点、全小写**的纯文本。

要求：
1. 恢复标点和大小写（添加 。！？，；： 等，并句首大写）。
2. 保持意群完整，不拆分语义单元。
3. 在句子级标点（。！？）或句首大写处强制分句。
4. 每句长度尽量接近 {options.TargetLength} 个字符，且不得超过 {options.MaxLength} 个字符（字符数包括标点和空格）。

重要：
- 只输出一个 JSON 数组，每个元素是一个句子字符串。
- **不要包含任何其他文本、解释、代码块标记（如 ```json 或 ```python）或任何额外内容。**
- 输出必须是纯 JSON 数组，例如：[""句子1"", ""句子2"", ...]。

输入文本（无标点、全小写）：
{cleanText}

输出（仅 JSON 数组）：
";
        }

        return $@"
You are a professional subtitle sentence splitting and text normalization expert. The input text is **uncapitalized and without punctuation**.

Requirements:
1. Restore punctuation and capitalization (add . ! ? , ; : and capitalize sentence starts).
2. Preserve phrase integrity – do not split semantic units.
3. Split at sentence-ending punctuation (. ! ?) or sentence-initial capitals.
4. Each sentence should be close to {options.TargetLength} characters and MUST NOT exceed {options.MaxLength} characters (characters include spaces and punctuation).

Important:
- Output ONLY a JSON array of strings.
- **DO NOT include any other text, explanation, code block markers (like ```json or ```python), or any extra content.**
- The response must be a valid JSON array, e.g., [""Sentence 1"", ""Sentence 2""].

Input text (no punctuation, all lowercase):
{cleanText}

Output (JSON array only):
";
    }

    // ---------- Parse response: extract the JSON array (strip extra content if present) ----------
    private List<string> ParseResponse(string responseText)
    {
        // First try direct deserialization
        try
        {
                 return JsonParser.Deserialize<List<string>>(responseText);
        }
        catch (JsonException)
        {
            // If direct deserialization fails, try extracting the JSON array
            var match = JsonArrayRegex.Match(responseText);
            if (match.Success)
            {
                var json = match.Value;
                  return JsonParser.Deserialize<List<string>>(json);
            }
            throw new FormatException("Response does not contain a valid JSON array.");
        }
    }

    // ---------- Main build method (with fallbacks) ----------
    private List<Sentence> BuildSentencesWithFallback(List<Word> words, List<string> sentenceTexts, SplitOptions options)
    {
        var wordList = words.OrderBy(w => w.Start).ToList();

        // Strategy 1: direct mapping by word count (if the totals match)
        var totalLlmWords = sentenceTexts.Sum(s => s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
        if (totalLlmWords == wordList.Count)
        {
            _logger?.LogDebug("LLM word count matches original, using direct mapping.");
            return BuildSentencesByCount(wordList, sentenceTexts, options);
        }

        _logger?.LogWarning("LLM word count ({TotalLlmWords}) differs from original ({TotalOriginal}). Attempting fuzzy alignment.",
            totalLlmWords, wordList.Count);

        // Strategy 2: fuzzy-match alignment
        var aligned = TryFuzzyAlignment(wordList, sentenceTexts, options);
        if (aligned != null)
        {
            _logger?.LogDebug("Fuzzy alignment succeeded, returning {Count} sentences.", aligned.Count);
            return aligned;
        }

        // Strategy 3: fall back to rule-based splitting
        _logger?.LogWarning("Fuzzy alignment failed. Falling back to rule-based splitting.");
        return FallbackSplit(words, options);
    }

    // ---------- Direct split by count ----------
    private List<Sentence> BuildSentencesByCount(List<Word> wordList, List<string> sentenceTexts, SplitOptions options)
    {
        var result = new List<Sentence>();
        var wordIndex = 0;

        for (var idx = 0; idx < sentenceTexts.Count; idx++)
        {
            var sentenceText = sentenceTexts[idx];
            var targetWords = sentenceText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var count = targetWords.Length;
            if (count == 0)
                continue;

            if (wordIndex + count > wordList.Count)
                count = wordList.Count - wordIndex;

            if (count <= 0)
                break;

            var sentenceWords = wordList.Skip(wordIndex).Take(count).ToList();
            wordIndex += count;

            result.Add(new Sentence
            {
                Text = sentenceText,  // use the LLM-generated normalized text
                Start = sentenceWords.First().Start,
                End = sentenceWords.Last().End,
                Words = sentenceWords
            });
        }

        // Remaining words form the last sentence (using the original joined text)
        if (wordIndex < wordList.Count)
        {
            var remaining = wordList.Skip(wordIndex).ToList();
            result.Add(new Sentence
            {
                Text = LanguageSupport.JoinMixed(remaining.Select(w => w.Text)),
                Start = remaining.First().Start,
                End = remaining.Last().End,
                Words = remaining
            });
        }

        return result;
    }

    // ---------- Fuzzy matching (sliding window) ----------
    private List<Sentence>? TryFuzzyAlignment(List<Word> wordList, List<string> sentenceTexts, SplitOptions options)
    {
        var result = new List<Sentence>();
        var wordIndex = 0;

        var llmWordSequences = sentenceTexts
            .Select(s => s.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(w => NormalizeWord(w))
                .Where(w => !string.IsNullOrEmpty(w))
                .ToList())
            .ToList();

        if (llmWordSequences.Any(seq => seq.Count == 0))
            return null;

        foreach (var llmSeq in llmWordSequences)
        {
            var remaining = wordList.Count - wordIndex;
            if (remaining == 0)
                break;

            var bestStart = -1;
            double bestScore = -1;

            var maxStart = Math.Min(wordIndex + llmSeq.Count * 2, wordList.Count);
            for (var start = wordIndex; start < maxStart; start++)
            {
                var windowLen = Math.Min(llmSeq.Count, wordList.Count - start);
                if (windowLen == 0) break;

                var matches = 0;
                for (var i = 0; i < windowLen && i < llmSeq.Count; i++)
                {
                    var origNorm = NormalizeWord(wordList[start + i].Text);
                    if (origNorm == llmSeq[i])
                        matches++;
                }
                var score = (double)matches / llmSeq.Count;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestStart = start;
                }

                if (bestScore == 1.0)
                    break;
            }

            if (bestStart < 0 || bestScore < MatchThreshold)
            {
                _logger?.LogWarning("Fuzzy match failed for sentence with score {Score}", bestScore);
                return null;
            }

            var takeCount = Math.Min(llmSeq.Count, wordList.Count - bestStart);
            // Merge all words from wordIndex to bestStart+takeCount so no words are lost
            var allWords = wordList.Skip(wordIndex).Take(bestStart + takeCount - wordIndex).ToList();

            var llmText = sentenceTexts[result.Count];

            result.Add(new Sentence
            {
                Text = llmText,
                Start = allWords.First().Start,
                End = allWords.Last().End,
                Words = allWords
            });

            wordIndex = bestStart + takeCount;
        }

        // Remaining words form the last sentence (using the original joined text)
        if (wordIndex < wordList.Count)
        {
            var remaining = wordList.Skip(wordIndex).ToList();
            result.Add(new Sentence
            {
                Text = LanguageSupport.JoinMixed(remaining.Select(w => w.Text)),
                Start = remaining.First().Start,
                End = remaining.Last().End,
                Words = remaining
            });
        }

        return result;
    }

    // ---------- Helpers ----------
    private static string NormalizeWord(string word)
    {
        return new string(word.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }

    // ---------- Final fallback: length-based rule splitting ----------
    private List<Sentence> FallbackSplit(List<Word> words, SplitOptions options)
    {
        var result = new List<Sentence>();
        var wordList = words.OrderBy(w => w.Start).ToList();
        var start = 0;
        var currentLen = 0;

        for (var i = 0; i < wordList.Count; i++)
        {
            var w = wordList[i];
            var wordLen = w.Text.Length + (i > start ? 1 : 0);
            if (currentLen + wordLen > options.TargetLength && i > start)
            {
                var slice = wordList.Skip(start).Take(i - start).ToList();
                result.Add(new Sentence
                {
                    Text = LanguageSupport.JoinMixed(slice.Select(x => x.Text)),
                    Start = slice.First().Start,
                    End = slice.Last().End,
                    Words = slice
                });
                start = i;
                currentLen = 0;
            }
            currentLen += wordLen;
        }

        if (start < wordList.Count)
        {
            var slice = wordList.Skip(start).ToList();
            result.Add(new Sentence
            {
                Text = LanguageSupport.JoinMixed(slice.Select(x => x.Text)),
                Start = slice.First().Start,
                End = slice.Last().End,
                Words = slice
            });
        }

        return result;
    }
}