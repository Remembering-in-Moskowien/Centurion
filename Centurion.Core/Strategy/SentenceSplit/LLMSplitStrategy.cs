using Centurion.Core.Abstractions.Strategy;
using Centurion.Core.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Centurion.Core.Strategy.SentenceSplit;

/// <summary>
/// 基于 LLM 的分句策略，完全信任 LLM 原始输出，无任何后处理修正或降级逻辑。
/// </summary>
public class LLMSplitStrategy : BaseSplitStrategy
{
    private readonly IChatClient _chatClient;
    private readonly ILogger<LLMSplitStrategy>? _logger;

    public LLMSplitStrategy(IChatClient chatClient, ILogger<LLMSplitStrategy>? logger = null)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _logger = logger;
    }

    public override async Task<List<Sentence>> Split(List<Word> words, SplitOptions options)
    {
        if (words.Count == 0)
            return [];

        var text = string.Join(" ", words.Select(w => w.Text));
        var prompt = BuildSplitPrompt(text, options);

        _logger?.LogDebug("Sending split request to LLM, text length: {Length}", text.Length);

        // 直接调用 LLM，不捕获异常，失败则向上抛出
        var response = await _chatClient.GetResponseAsync(prompt);

        if (response.Messages.Count == 0)
            throw new InvalidOperationException("LLM returned an empty response.");

        var messageText = response.Messages[0].Text;
        if (string.IsNullOrEmpty(messageText))
            throw new InvalidOperationException("LLM response message text is empty.");

        // 解析 JSON
        var splitResult = ParseResponse(messageText);

        // 构建句子，直接按单词数量顺序分配，不进行任何长度修正或二次拆分
        return BuildSentences(words, splitResult);
    }

    private string BuildSplitPrompt(string text, SplitOptions options)
    {
        var lang = options.Language?.ToLowerInvariant() ?? "en";

        if (lang == "zh" || lang == "zh-cn" || lang == "zh-tw")
        {
            return $@"
你是一个专业字幕分句助手。请将以下中文文本按语义分割为合适的句子。

关键规则：
- 每句的长度必须不超过 {options.MaxLength} 个字符。
- 每句长度尽量接近 {options.TargetLength} 个字符。
- 如果某句超过 {options.MaxLength}，你必须将其进一步拆分。
- 优先在标点处断句（。！？，；：等）。
- 确保包含所有单词，不要遗漏任何词。
- 返回 JSON 数组。

文本：
{text}

输出格式（JSON 数组）：
[""句子1"", ""句子2"", ...]
";
        }
        else
        {
            return $@"
You are a professional subtitle sentence splitter. Split the following text into appropriate sentences based on semantic units.

CRITICAL RULES:
- Each sentence MUST NOT exceed {options.MaxLength} characters.
- Ideally, each sentence should be around {options.TargetLength} characters.
- If a segment exceeds {options.MaxLength}, you MUST split it further.
- Prefer breaking at punctuation marks (. ! ? , ; :).
- Ensure you include ALL words in the output, do not omit any word.
- Return a JSON array of sentence strings.

Text:
{text}

Output format (JSON array):
[""Sentence 1"", ""Sentence 2"", ...]
";
        }
    }

    private List<string> ParseResponse(string responseText)
    {
        var jsonStart = responseText.IndexOf('[');
        var jsonEnd = responseText.LastIndexOf(']');
        if (jsonStart >= 0 && jsonEnd > jsonStart)
        {
            var json = responseText.Substring(jsonStart, jsonEnd - jsonStart + 1);
            return JsonSerializer.Deserialize<List<string>>(json)
                   ?? throw new InvalidOperationException("Failed to deserialize JSON response.");
        }
        throw new FormatException("Response does not contain a valid JSON array.");
    }

    /// <summary>
    /// 按 LLM 返回的句子列表顺序，直接从原始单词列表中截取对应数量的单词。
    /// 不进行标点匹配、不去除标点、不做任何修正。
    /// </summary>
    private List<Sentence> BuildSentences(List<Word> words, List<string> sentenceTexts)
    {
        var result = new List<Sentence>();
        var wordList = words.OrderBy(w => w.Start).ToList();
        int wordIndex = 0;

        foreach (var sentenceText in sentenceTexts)
        {
            var targetWords = sentenceText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int count = targetWords.Length;
            if (count == 0)
                continue;

            if (wordIndex + count > wordList.Count)
                throw new InvalidOperationException(
                    $"LLM returned more words than available. " +
                    $"Expected {count} words starting at index {wordIndex}, but only {wordList.Count - wordIndex} left.");

            var sentenceWords = wordList.Skip(wordIndex).Take(count).ToList();
            wordIndex += count;

            result.Add(new Sentence
            {
                Text = string.Join(" ", sentenceWords.Select(w => w.Text)),
                Start = sentenceWords.First().Start,
                End = sentenceWords.Last().End,
                Words = sentenceWords
            });
        }

        if (wordIndex < wordList.Count)
        {
            throw new InvalidOperationException(
                $"LLM returned fewer words than available. Remaining {wordList.Count - wordIndex} words were not assigned.");
        }

        return result;
    }
}