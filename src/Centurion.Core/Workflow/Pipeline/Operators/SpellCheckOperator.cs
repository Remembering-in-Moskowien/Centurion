using System.Text.Json;
using Centurion.Abstractions.Pipeline;
using Centurion.Core.Capabilities.Infrastructure;using Centurion.Core.Processing.SpellCheck;using Centurion.Models.Workflow;
using Microsoft.Extensions.Logging;
using WeCantSpell.Hunspell;
using Centurion.Models.Console;
namespace Centurion.Core.Workflow.Pipeline.Operators;

/// <summary>
/// Hunspell spell-check operator: spell-checks the corrected subtitle text,
/// emits warnings for suspicious words and writes a {output}.spellcheck.json report; if the dictionary is unavailable, it only warns and skips.
/// </summary>
public sealed class SpellCheckOperator(
    HunspellSpellChecker checker,
    ILogger<SpellCheckOperator> logger)
    : PipelineOperatorBase<SpellCheckOperator>(logger)
{
    /// <summary>Display name of the operator in the pipeline.</summary>
    public override string Name => "Spell Check";

    /// <summary>
    /// Runs spell checking: ensure dictionary -> tokenize and check each sentence -> summarize warnings -> write the report.
    /// </summary>
    /// <param name="context">Workflow context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public override async Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken)
    {
        var dictionaryPrefix = context.Config.HunspellDictionary ?? "en_US";
        var dictionary = await checker.EnsureDictionaryAsync(dictionaryPrefix, cancellationToken);
        if (dictionary is null)
        {
            LogWarning($"Spell check skipped: dictionary '{dictionaryPrefix}' unavailable.");
            return;
        }

        var sentences = context.State.CurrentSentences;
        var issues = new List<SpellCheckIssue>();
        for (var i = 0; i < sentences.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = sentences[i].CleanedText ?? sentences[i].Text;
            issues.AddRange(HunspellSpellChecker.CheckSentence(i, text, dictionary));
        }

        context.State.Extensions["SpellCheckIssues"] = issues;

        if (issues.Count == 0)
        {
            LogInfo($"Spell check passed: no suspicious words in {sentences.Count} sentences.");
            return;
        }

        // Console warning: summarize at most the first 8 entries to avoid flooding the console
        var preview = string.Join("; ", issues.Take(8).Select(i => $"'{i.Word}' (line {i.SentenceIndex + 1})"));
        var suffix = issues.Count > 8 ? $" … (+{issues.Count - 8} more)" : "";
        ConsoleServices.Output.WriteWarning(
            ConsoleServices.T("Spell check found {0} suspicious word(s) in the subtitles: {1}", issues.Count, preview + suffix));
        LogWarning($"Spell check found {issues.Count} suspicious word(s): {preview}{suffix}");

        // Write the report to the output directory
        var outputPath = context.Config.OutputFilePath;
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            var reportPath = Path.ChangeExtension(outputPath, ".spellcheck.json");
            await File.WriteAllTextAsync(
                reportPath,
                JsonSerializer.Serialize(issues, new JsonSerializerOptions { WriteIndented = true }),
                cancellationToken);
            ConsoleServices.Output.WriteInfo(ConsoleServices.T("Spell check report: {0}", reportPath));
        }
    }
}
