using System.Text;
using Centurion.Abstractions.Strategy;
using Centurion.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Centurion.Abstractions.Utils;
using Centurion.Core.Utils.Parsing;
namespace Centurion.Core.Workflow.Strategy.Translation;

/// <summary>
/// 基于大语言模型（OpenAI/Ollama）的翻译策略：
/// 逐批调用 LLM 把源句翻译到目标语言并填充 <see cref="Sentence.TranslatedText"/>，
/// 保持每句时间轴与词级明细不变。
/// 支持术语表强制约束与目标语言台本措辞参考；
/// 目标台本行数与源句一致时按行号 1:1 直接对齐采用（纯文本对齐，不调用 LLM）。
/// 单批失败时降级为逐句重试，仍失败的句子保留原文并记录警告。
/// </summary>
public class LLMTranslationStrategy : ITranslationStrategy
{
    private readonly IChatClient _chatClient;
    private readonly ILogger<LLMTranslationStrategy>? _logger;

    /// <summary>创建基于 LLM 的翻译策略实例。</summary>
    /// <param name="chatClient">用于调用大语言模型的对话客户端。</param>
    /// <param name="logger">可选的日志记录器，为 null 时不记录日志。</param>
    public LLMTranslationStrategy(IChatClient chatClient, ILogger<LLMTranslationStrategy>? logger = null)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _logger = logger;
    }

    /// <summary>策略的显示名称。</summary>
    public string StrategyName => "LLM";

    /// <summary>
    /// 执行翻译：先尝试目标台本 1:1 对齐（数量一致时），否则分批调用 LLM 翻译。
    /// </summary>
    /// <param name="sentences">待翻译的句子列表（就地填充译文，时间轴不变）。</param>
    /// <param name="options">翻译选项：目标语言、术语表、目标语言台本等。</param>
    /// <param name="cancellationToken">用于取消翻译过程的取消标记。</param>
    /// <returns>翻译完成后的句子列表。</returns>
    public async Task<List<Sentence>> TranslateAsync(
        List<Sentence> sentences,
        TranslationOptions options,
        CancellationToken cancellationToken = default)
    {
        if (sentences.Count == 0)
            return sentences;

        // 1) 目标语言台本 1:1 对齐：行数与源句一致时直接采用台本措辞（纯文本对齐）
        if (AlignToScript(sentences, options.TargetScriptLines))
        {
            _logger?.LogInformation("Target script matches {Count} sentences; using 1:1 script alignment.", sentences.Count);
            return sentences;
        }

        if (options.TargetScriptLines.Count > 0)
        {
            _logger?.LogWarning(
                "Target script line count ({ScriptLines}) does not match sentence count ({Sentences}); using LLM translation with script as wording reference.",
                options.TargetScriptLines.Count, sentences.Count);
        }

        // 2) 分批 LLM 翻译：批次间互相独立（每批独立 prompt、独立填充译文），
        //    以 MaxConcurrency 并行执行，翻译结果与串行逐批完全一致
        var batchSize = Math.Max(1, options.BatchSize);
        var batches = new List<(int Offset, List<Sentence> Batch)>();
        for (var offset = 0; offset < sentences.Count; offset += batchSize)
            batches.Add((offset, sentences.Skip(offset).Take(batchSize).ToList()));

        using var gate = new SemaphoreSlim(Math.Max(1, options.MaxConcurrency));
        await Task.WhenAll(batches.Select(async batch =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                await TranslateBatchAsync(batch.Batch, batch.Offset, options, cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        }));

        return sentences;
    }

    private async Task TranslateBatchAsync(
        List<Sentence> batch,
        int offset,
        TranslationOptions options,
        CancellationToken cancellationToken)
    {
        // 批次内索引 → 句子
        var prompt = BuildPrompt(batch, options);

        try
        {
            var response = await _chatClient.GetResponseAsync(prompt, cancellationToken: cancellationToken);
            var messageText = response?.Messages.FirstOrDefault()?.Text;
            if (string.IsNullOrWhiteSpace(messageText))
                throw new InvalidOperationException("LLM translation returned empty response.");

            var items = ParseResponse(messageText);
            if (items.Count == 0)
                throw new InvalidOperationException("LLM translation returned no items.");

            foreach (var item in items)
            {
                if (item.Id < 0 || item.Id >= batch.Count)
                    continue;
                var translation = item.Translation?.Trim();
                if (!string.IsNullOrWhiteSpace(translation))
                    batch[item.Id].TranslatedText = translation;
            }

            var translated = batch.Count(s => !string.IsNullOrWhiteSpace(s.TranslatedText));
            _logger?.LogInformation("Translated {Translated}/{Total} sentences in batch starting at {Offset}.",
                translated, batch.Count, offset);

            if (translated < batch.Count)
            {
                _logger?.LogWarning("Batch starting at {Offset}: {Missed} sentence(s) missing translation; retrying individually.",
                    offset, batch.Count - translated);
                await RetryMissingAsync(batch, options, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "LLM translation batch failed at offset {Offset}; retrying individually.", offset);
            await RetryMissingAsync(batch, options, cancellationToken);
        }
    }

    /// <summary>对批内未译句子逐句重试；仍失败的保留原文并记录警告。</summary>
    private async Task RetryMissingAsync(List<Sentence> batch, TranslationOptions options, CancellationToken cancellationToken)
    {
        for (var i = 0; i < batch.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(batch[i].TranslatedText))
                continue;

            try
            {
                var single = new List<Sentence> { batch[i] };
                var prompt = BuildPrompt(single, options);
                var response = await _chatClient.GetResponseAsync(prompt, cancellationToken: cancellationToken);
                var messageText = response?.Messages.FirstOrDefault()?.Text;
                var items = string.IsNullOrWhiteSpace(messageText) ? [] : ParseResponse(messageText);
                var item = items.FirstOrDefault();
                if (item is not null && !string.IsNullOrWhiteSpace(item.Translation?.Trim()))
                    batch[i].TranslatedText = item.Translation.Trim();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.LogWarning(ex, "Individual translation failed for sentence: {Text}", batch[i].Text);
            }

            if (string.IsNullOrWhiteSpace(batch[i].TranslatedText))
                _logger?.LogWarning("Keeping original text for untranslatable sentence: {Text}", batch[i].Text);
        }
    }

    /// <summary>
    /// 目标语言台本 1:1 对齐：台本行数与源句数一致时，按行号直接把台本行作为译文填入。
    /// 行数不一致时返回 false，交由 LLM 翻译。
    /// </summary>
    /// <param name="sentences">待翻译句子（就地填充译文）。</param>
    /// <param name="scriptLines">目标语言台本行。</param>
    /// <returns>是否完成 1:1 对齐。</returns>
    internal static bool AlignToScript(List<Sentence> sentences, IReadOnlyList<string> scriptLines)
    {
        if (scriptLines.Count != sentences.Count)
            return false;

        for (var i = 0; i < sentences.Count; i++)
            sentences[i].TranslatedText = scriptLines[i];
        return true;
    }

    /// <summary>
    /// 构造翻译提示词：角色设定、源/目标语言、术语表约束、台本措辞参考与严格 JSON 输出要求。
    /// </summary>
    /// <param name="sentences">本批待翻译句子。</param>
    /// <param name="options">翻译选项。</param>
    /// <returns>完整的提示词字符串。</returns>
    internal static string BuildPrompt(List<Sentence> sentences, TranslationOptions options)
    {
        var sb = new StringBuilder();

        sb.Append("You are a professional subtitle translator. Translate each subtitle line into ")
          .Append(options.TargetLanguage)
          .AppendLine(". Keep the line count and order identical.");

        if (options.Glossary.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("MANDATORY glossary (use these target terms whenever the source term appears):");
            foreach (var pair in options.Glossary)
                sb.AppendLine($"  {pair.Key} -> {pair.Value}");
        }

        if (options.TargetScriptLines.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Reference target-language script (align wording and names with it when possible):");
            foreach (var line in options.TargetScriptLines)
                sb.AppendLine($"  {line}");
        }

        sb.AppendLine();
        sb.AppendLine("Output ONLY a JSON array, one object per line, with \"id\" matching the input order index (0-based) and \"translation\" holding the translated text. No extra text, no code fences.");
        sb.AppendLine();

        var payload = sentences
            .Select((sentence, index) => new { id = index, text = sentence.Text })
            .ToList();
        sb.Append(JsonConvert.SerializeObject(payload));

        return sb.ToString();
    }

    /// <summary>解析 LLM 返回的翻译 JSON 数组。</summary>
    /// <param name="responseText">LLM 原始响应文本。</param>
    /// <returns>解析出的翻译条目列表。</returns>
    internal static List<TranslationItem> ParseResponse(string responseText)
    {
        try
        {
            return JsonParser.Deserialize<List<TranslationItem>>(responseText) ?? [];
        }
        catch (JsonException)
        {
            // 兼容代码块包裹等多余内容：提取首个 JSON 数组
            var start = responseText.IndexOf('[');
            var end = responseText.LastIndexOf(']');
            if (start < 0 || end <= start)
                return [];

            var json = responseText[start..(end + 1)];
            return JsonParser.Deserialize<List<TranslationItem>>(json) ?? [];
        }
    }

    /// <summary>LLM 翻译响应的单条条目（id 对应输入批次索引，translation 为译文）。</summary>
    public sealed class TranslationItem
    {
        /// <summary>对应输入批次内的 0 基索引。</summary>
        [JsonProperty("id")]
        public int Id { get; set; }

        /// <summary>翻译后的文本。</summary>
        [JsonProperty("translation")]
        public string? Translation { get; set; }
    }
}
