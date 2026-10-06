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
/// Translation strategy based on a large language model (OpenAI/Ollama):
/// calls the LLM batch by batch to translate source sentences into the target language and fill <see cref="Sentence.TranslatedText"/>,
/// keeping each sentence's timeline and word-level details unchanged.
/// Supports mandatory glossary constraints and target-language script wording reference;
/// when the target script line count matches the source sentence count, it adopts script lines 1:1 by line number (plain-text alignment, no LLM call).
/// On a batch failure it falls back to retrying sentence by sentence; sentences still failing keep the original text and log a warning.
/// </summary>
public class LLMTranslationStrategy : ITranslationStrategy
{
    private readonly IChatClient _chatClient;
    private readonly ILogger<LLMTranslationStrategy>? _logger;

    /// <summary>Creates an LLM-based translation strategy instance.</summary>
    /// <param name="chatClient">The chat client used to call the large language model.</param>
    /// <param name="logger">Optional logger; when null, nothing is logged.</param>
    public LLMTranslationStrategy(IChatClient chatClient, ILogger<LLMTranslationStrategy>? logger = null)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _logger = logger;
    }

    /// <summary>Display name of the strategy.</summary>
    public string StrategyName => "LLM";

    /// <summary>
    /// Performs translation: first tries 1:1 target-script alignment (when counts match), otherwise calls the LLM in batches.
    /// </summary>
    /// <param name="sentences">The sentences to translate (their translations are filled in place; the timeline is unchanged).</param>
    /// <param name="options">Translation options: target language, glossary, target-language script, etc.</param>
    /// <param name="cancellationToken">Token used to cancel the translation process.</param>
    /// <returns>The sentence list after translation.</returns>
    public async Task<List<Sentence>> TranslateAsync(
        List<Sentence> sentences,
        TranslationOptions options,
        CancellationToken cancellationToken = default)
    {
        if (sentences.Count == 0)
            return sentences;

        // 1) 1:1 target-script alignment: when the line count matches the source sentences, adopt the script wording directly (plain-text alignment)
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

        // 2) Batched LLM translation: batches are independent (each has its own prompt and fills its own translations),
        //    run in parallel up to MaxConcurrency; the result is identical to serial, batch-by-batch translation
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
        // Batch index -> sentence
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

    /// <summary>Retries each untranslated sentence in the batch one by one; sentences still failing keep the original text and log a warning.</summary>
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
    /// 1:1 target-script alignment: when the script line count matches the source sentence count, script lines are
    /// filled in directly as translations by line number. Returns false when the counts differ, falling back to LLM translation.
    /// </summary>
    /// <param name="sentences">The sentences to translate (translations filled in place).</param>
    /// <param name="scriptLines">The target-language script lines.</param>
    /// <returns>Whether the 1:1 alignment was completed.</returns>
    internal static bool AlignToScript(List<Sentence> sentences, IReadOnlyList<string> scriptLines)
    {
        if (scriptLines.Count != sentences.Count)
            return false;

        for (var i = 0; i < sentences.Count; i++)
            sentences[i].TranslatedText = scriptLines[i];
        return true;
    }

    /// <summary>
    /// Builds the translation prompt: role setting, source/target language, glossary constraints, script wording reference, and strict JSON output requirement.
    /// </summary>
    /// <param name="sentences">The sentences to translate in this batch.</param>
    /// <param name="options">Translation options.</param>
    /// <returns>The complete prompt string.</returns>
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

    /// <summary>Parses the translation JSON array returned by the LLM.</summary>
    /// <param name="responseText">The raw LLM response text.</param>
    /// <returns>The list of parsed translation items.</returns>
    internal static List<TranslationItem> ParseResponse(string responseText)
    {
        try
        {
            return JsonParser.Deserialize<List<TranslationItem>>(responseText) ?? [];
        }
        catch (JsonException)
        {
            // Tolerate extra content such as a code fence: extract the first JSON array
            var start = responseText.IndexOf('[');
            var end = responseText.LastIndexOf(']');
            if (start < 0 || end <= start)
                return [];

            var json = responseText[start..(end + 1)];
            return JsonParser.Deserialize<List<TranslationItem>>(json) ?? [];
        }
    }

    /// <summary>A single entry in the LLM translation response (id is the input batch index, translation is the translated text).</summary>
    public sealed class TranslationItem
    {
        /// <summary>The 0-based index within the input batch.</summary>
        [JsonProperty("id")]
        public int Id { get; set; }

        /// <summary>The translated text.</summary>
        [JsonProperty("translation")]
        public string? Translation { get; set; }
    }
}
