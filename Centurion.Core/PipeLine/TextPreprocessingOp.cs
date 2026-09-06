using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Centurion.Core.Abstractions;
using Centurion.Core.Models;
using Humanizer;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.PipeLine;

public class TextPreprocessingOp(
    ILogger<TextPreprocessingOp> logger) : PipelineOperatorBase
{
    private static readonly Regex PunctuationPattern = new(@"[\p{P}\p{S}]", RegexOptions.Compiled);
    private static readonly Regex WhitespacePattern = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex NumberPattern = new(
        @"\b(?:\d{1,3}(?:,\d{3})+|\d+)\b",
        RegexOptions.Compiled);

    private static readonly Dictionary<string, string> BuiltInAbbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Dr."] = "Doctor",
        ["Mr."] = "Mister",
        ["Mrs."] = "Missus",
        ["Ms."] = "Miss",
        ["Prof."] = "Professor",
        ["St."] = "Street"
    };

    public override string Name => "Text Cleaning for Alignment";

    public override Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Config.EnableTextCleaning)
        {
            logger.LogInformation("Text cleaning is disabled.");
            context.State.Extensions["TextCleaned"] = false;
            return Task.CompletedTask;
        }

        var sentences = context.State.ScriptSentences.Count > 0
            ? context.State.ScriptSentences
            : context.State.SplitSentences;
        if (sentences is null || sentences.Count == 0)
        {
            logger.LogInformation("No sentences available for text cleaning.");
            context.State.Extensions["TextCleaned"] = false;
            return Task.CompletedTask;
        }

        var abbreviations = LoadAbbreviations(context);
        OnProgress(0, "Preparing text cleaning...");

        for (var index = 0; index < sentences.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sentence = sentences[index];

            try
            {
                logger.LogDebug("Cleaning sentence {Index}/{Total}.", index + 1, sentences.Count);
                sentence.CleanedText = CleanText(sentence.Text, context.Config, abbreviations);
            }
            catch (Exception ex)
            {
                sentence.CleanedText = null;
                var message = $"Failed to clean sentence {index + 1}: {ex.Message}";
                logger.LogWarning(ex, "{Message}", message);
                context.State.Warnings.Add(message);
                context.State.Errors.Add(message);
            }

            OnProgress((index + 1) * 100 / sentences.Count, $"Cleaning sentence {index + 1}/{sentences.Count}");
        }

        context.State.Extensions["TextCleaned"] = true;
        logger.LogInformation("Text cleaning completed for {Count} sentences.", sentences.Count);
        return Task.CompletedTask;
    }

    private Dictionary<string, string> LoadAbbreviations(SubtitleWorkflowContext context)
    {
        var abbreviations = new Dictionary<string, string>(BuiltInAbbreviations, StringComparer.OrdinalIgnoreCase);
        var path = context.Config.CustomDictPath;
        if (string.IsNullOrWhiteSpace(path))
            return abbreviations;

        try
        {
            var custom = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
            if (custom is not null)
            {
                foreach (var pair in custom)
                    abbreviations[pair.Key] = pair.Value;
            }
        }
        catch (Exception ex)
        {
            var message = $"Failed to load custom abbreviation dictionary '{path}': {ex.Message}";
            logger.LogWarning(ex, "{Message}", message);
            context.State.Warnings.Add(message);
            context.State.Errors.Add(message);
        }

        return abbreviations;
    }

    internal static string CleanText(
        string text,
        WorkflowConfig config,
        IReadOnlyDictionary<string, string>? abbreviations = null)
    {
        var result = text ?? string.Empty;

        if (config.RemovePunctuation)
            result = PunctuationPattern.Replace(result, string.Empty);

        if (config.ExpandNumbers)
        {
            // 匹配带千位分隔符的整数（如 500,000）或普通整数（如 123）
            result = NumberPattern.Replace(result, match =>
            {
                // 移除逗号，得到纯数字字符串
                var raw = match.Value.Replace(",", "");
                if (!long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
                    return match.Value; // 解析失败则保留原文

                return number.ToWords(CultureInfo.GetCultureInfo("en-US"));
            });
        }

        if (config.ExpandAbbreviations && abbreviations is not null)
        {
            foreach (var pair in abbreviations.OrderByDescending(pair => pair.Key.Length))
                result = Regex.Replace(result, Regex.Escape(pair.Key), pair.Value, RegexOptions.IgnoreCase);
        }

        result = result.ToLowerInvariant();
        return WhitespacePattern.Replace(result, " ").Trim();
    }
}