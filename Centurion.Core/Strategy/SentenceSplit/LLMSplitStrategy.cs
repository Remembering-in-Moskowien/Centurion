using System.Text.Json;
using System.Text.RegularExpressions;
using Centurion.Core.Abstractions.Strategy;
using Centurion.Core.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Strategy.SentenceSplit;

/// <summary>
/// 基于 LLM 的分句策略：输入先去除标点和大小写，让 LLM 恢复并断句。
/// 带智能兜底：若 LLM 输出异常，使用模糊匹配将句子边界对齐到原始单词序列。
/// </summary>
public class LLMSplitStrategy : BaseSplitStrategy
{
    private readonly IChatClient _chatClient;
    private readonly ILogger<LLMSplitStrategy>? _logger;
    private const double MatchThreshold = 0.5;

    // 用于提取 JSON 数组的正则表达式（支持纯数组或 Markdown 代码块）
    private static readonly Regex JsonArrayRegex = new(@"\[\s*""(?:[^""\\]|\\.)*""\s*(?:,\s*""(?:[^""\\]|\\.)*""\s*)*\]", RegexOptions.Compiled);

    public LLMSplitStrategy(IChatClient chatClient, ILogger<LLMSplitStrategy>? logger = null)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _logger = logger;
    }

    public override async Task<List<Sentence>> Split(List<Word> words, SplitOptions options)
    {
        if (words.Count == 0)
            return [];

        // ----- 预处理：去除标点和大小写 -----
        var cleanWords = words.Select(w => new string(w.Text.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant())
                              .Where(w => !string.IsNullOrEmpty(w))
                              .ToList();
        var cleanText = string.Join(" ", cleanWords);
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

            // 尝试解析 JSON（若包含多余文本则清理）
            var splitResult = ParseResponse(messageText);
            return BuildSentencesWithFallback(words, splitResult, options);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "LLM request failed. Falling back to rule-based splitting.");
            return FallbackSplit(words, options);
        }
    }

    // ---------- 强化提示词：强制要求纯 JSON 输出 ----------
    private string BuildSplitPrompt(string cleanText, SplitOptions options)
    {
        var lang = options.Language?.ToLowerInvariant() ?? "en";

        if (lang == "zh" || lang == "zh-cn" || lang == "zh-tw")
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

    // ---------- 解析响应：提取 JSON 数组（若有多余内容则清理） ----------
    private List<string> ParseResponse(string responseText)
    {
        // 先尝试直接反序列化
        try
        {
            return JsonSerializer.Deserialize<List<string>>(responseText)
                   ?? throw new InvalidOperationException("Deserialized to null.");
        }
        catch (JsonException)
        {
            // 如果直接反序列化失败，尝试提取 JSON 数组
            var match = JsonArrayRegex.Match(responseText);
            if (match.Success)
            {
                var json = match.Value;
                return JsonSerializer.Deserialize<List<string>>(json)
                       ?? throw new InvalidOperationException("Failed to deserialize extracted JSON.");
            }
            throw new FormatException("Response does not contain a valid JSON array.");
        }
    }

    // ---------- 主构建方法（含兜底） ----------
    private List<Sentence> BuildSentencesWithFallback(List<Word> words, List<string> sentenceTexts, SplitOptions options)
    {
        var wordList = words.OrderBy(w => w.Start).ToList();

        // 策略1：直接按单词数量映射（如果总数一致）
        var totalLlmWords = sentenceTexts.Sum(s => s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
        if (totalLlmWords == wordList.Count)
        {
            _logger?.LogDebug("LLM word count matches original, using direct mapping.");
            return BuildSentencesByCount(wordList, sentenceTexts);
        }

        _logger?.LogWarning("LLM word count ({TotalLlmWords}) differs from original ({TotalOriginal}). Attempting fuzzy alignment.",
            totalLlmWords, wordList.Count);

        // 策略2：模糊匹配对齐
        var aligned = TryFuzzyAlignment(wordList, sentenceTexts);
        if (aligned != null)
        {
            _logger?.LogDebug("Fuzzy alignment succeeded, returning {Count} sentences.", aligned.Count);
            return aligned;
        }

        // 策略3：降级到规则分句
        _logger?.LogWarning("Fuzzy alignment failed. Falling back to rule-based splitting.");
        return FallbackSplit(words, options);
    }

    // ---------- 直接按数量切分 ----------
    private List<Sentence> BuildSentencesByCount(List<Word> wordList, List<string> sentenceTexts)
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
                Text = sentenceText,  // 使用 LLM 生成的规范化文本
                Start = sentenceWords.First().Start,
                End = sentenceWords.Last().End,
                Words = sentenceWords
            });
        }

        // 剩余单词作为最后一句（使用原始拼接文本）
        if (wordIndex < wordList.Count)
        {
            var remaining = wordList.Skip(wordIndex).ToList();
            result.Add(new Sentence
            {
                Text = string.Join(" ", remaining.Select(w => w.Text)),
                Start = remaining.First().Start,
                End = remaining.Last().End,
                Words = remaining
            });
        }

        return result;
    }

    // ---------- 模糊匹配（滑动窗口） ----------
    private List<Sentence>? TryFuzzyAlignment(List<Word> wordList, List<string> sentenceTexts)
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
            // 合并从 wordIndex 到 bestStart+takeCount 的所有单词，保证不丢词
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

        // 剩余单词作为最后一句（使用原始拼接文本）
        if (wordIndex < wordList.Count)
        {
            var remaining = wordList.Skip(wordIndex).ToList();
            result.Add(new Sentence
            {
                Text = string.Join(" ", remaining.Select(w => w.Text)),
                Start = remaining.First().Start,
                End = remaining.Last().End,
                Words = remaining
            });
        }

        return result;
    }

    // ---------- 辅助方法 ----------
    private static string NormalizeWord(string word)
    {
        return new string(word.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }

    // ---------- 最终降级：基于长度的规则分句 ----------
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
                    Text = string.Join(" ", slice.Select(x => x.Text)),
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
                Text = string.Join(" ", slice.Select(x => x.Text)),
                Start = slice.First().Start,
                End = slice.Last().End,
                Words = slice
            });
        }

        return result;
    }
}