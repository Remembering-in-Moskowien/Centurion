using System.Text.Json;
using Centurion.Abstractions.Pipeline;
using Centurion.Core.Capabilities.Infrastructure;using Centurion.Core.Processing.SpellCheck;using Centurion.Models.Workflow;
using Microsoft.Extensions.Logging;
using WeCantSpell.Hunspell;
using Centurion.Models.Console;
namespace Centurion.Core.Workflow.Pipeline.Operators;

/// <summary>
/// Hunspell 拼写检查算子：对校正后的字幕文本做拼写检查，
/// 将可疑词以警告输出并写入 {输出}.spellcheck.json 报告；词典不可用时仅警告并跳过。
/// </summary>
public sealed class SpellCheckOperator(
    HunspellSpellChecker checker,
    ILogger<SpellCheckOperator> logger)
    : PipelineOperatorBase<SpellCheckOperator>(logger)
{
    /// <summary>算子在管道中的显示名称。</summary>
    public override string Name => "Spell Check";

    /// <summary>
    /// 执行拼写检查：确保词典 → 逐句分词检查 → 汇总警告 → 报告落盘。
    /// </summary>
    /// <param name="context">工作流上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
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

        // 控制台警告：最多汇总前 8 条，避免刷屏
        var preview = string.Join("; ", issues.Take(8).Select(i => $"'{i.Word}' (line {i.SentenceIndex + 1})"));
        var suffix = issues.Count > 8 ? $" … (+{issues.Count - 8} more)" : "";
        ConsoleServices.Output.WriteWarning(
            ConsoleServices.T("Spell check found {0} suspicious word(s) in the subtitles: {1}", issues.Count, preview + suffix));
        LogWarning($"Spell check found {issues.Count} suspicious word(s): {preview}{suffix}");

        // 报告写入输出目录
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
