using Centurion.Cli.Commands.Settings;
using Centurion.Core.Infrastructure;
using Centurion.Abstractions;
using Centurion.Abstractions.Factories;
using Centurion.Abstractions.Strategy;
using Centurion.Core.Pipeline.Operators;
using Centurion.Core.Utils;
using Centurion.Models.Ass;
using Centurion.Models.Workflow;
using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;

namespace Centurion.Cli.Commands;

/// <summary>
/// Translation: translate an existing subtitle file into a target language.
/// Only performs text-level translation alignment — the timeline and word details are kept unchanged.
/// Supports LLM strategy, a glossary file, and a target-language script (1:1 alignment when counts match).
/// </summary>
public sealed class TranslateCommand(
    ConvertParseOperator convertParseOp,
    ITranslationStrategyFactory strategyFactory,
    ILogger<TranslateCommand> logger) : AsyncCommand<TranslateSettings>
{
    /// <summary>
    /// 执行翻译：解析已有字幕 → 按所选策略翻译文本 → 写出目标语言（或双语）字幕。
    /// 时间轴保持不变，只做文本层翻译对齐。
    /// </summary>
    /// <param name="context">Spectre 命令上下文。</param>
    /// <param name="settings">翻译命令选项。</param>
    /// <param name="ct">取消令牌。</param>
    protected override async Task<int> ExecuteAsync(CommandContext context, TranslateSettings settings, CancellationToken ct)
    {
        try
        {
            var subtitlePath = settings.SubtitleFile.FullName;
            if (!File.Exists(subtitlePath))
                throw new FileNotFoundException($"Subtitle file not found: {subtitlePath}", subtitlePath);
            if (string.IsNullOrWhiteSpace(settings.TargetLanguage))
                throw new ArgumentException("--target-language is required.");

            var outputPath = settings.OutputFile?.FullName ??
                             Path.ChangeExtension(subtitlePath, ".translated.ass");

            var config = new WorkflowConfig
            {
                InputFilePath = subtitlePath,
                SubtitleFilePath = subtitlePath,
                OutputFilePath = outputPath,
                Language = settings.SourceLanguage ?? "auto",
                TargetLanguage = settings.TargetLanguage,
                TranslationStrategy = settings.Strategy,
                TranslationModel = settings.Model,
                TranslationApiKey = settings.ApiKey,
                GlossaryPath = settings.Glossary?.FullName,
                TargetScriptPath = settings.TargetScript?.FullName,
                Bilingual = settings.Bilingual,
                KaraokeMode = settings.Karaoke
            };

            var workflowContext = new SubtitleWorkflowContext(config);

            // 1) 解析输入字幕（时间轴保持不变）
            await convertParseOp.ExecuteAsync(workflowContext, ct);
            var sentences = workflowContext.State.CurrentSentences;

            // 2) 加载术语表与目标语言台本
            var glossary = GlossaryLoader.Load(settings.Glossary?.FullName, logger);
            var targetScriptLines = LoadScriptLines(settings.TargetScript?.FullName, logger);

            // 3) 创建翻译策略并执行（LLM 分批翻译；台本行数一致时 1:1 对齐采用）
            var strategy = strategyFactory.Create(settings.Strategy, settings.Model, settings.ApiKey);
            logger.LogInformation("Using translation strategy: {Strategy}", strategy.StrategyName);

            var options = new TranslationOptions
            {
                SourceLanguage = settings.SourceLanguage ?? "auto",
                TargetLanguage = settings.TargetLanguage,
                Glossary = glossary,
                TargetScriptLines = targetScriptLines
            };

            await strategy.TranslateAsync(sentences, options, ct);
            workflowContext.State.TranslatedSentences = sentences;
            workflowContext.State.CurrentSentences = sentences;
            workflowContext.State.IsTranslated = true;

            // 4) 输出目标语言（或双语）字幕
            var assDoc = AssSubBuilder.FromWorkflow(workflowContext).Build();
            await File.WriteAllTextAsync(outputPath, assDoc.ToString(), ct);

            // 5) 输出富上下文 JSON（配置 + 翻译结果 + 诊断）
            var contextPath = await WorkflowContextDumper.WriteAsync(workflowContext, "translate", outputPath, ct);

            var translatedCount = sentences.Count(s => !string.IsNullOrWhiteSpace(s.TranslatedText));
            ConsoleServices.Output.WriteMarkupLine($"[green]Translation completed: {outputPath}[/]");
            ConsoleServices.Output.WriteMarkupLine($"[grey]Translated {translatedCount}/{sentences.Count} sentences -> {settings.TargetLanguage}[/]");
            ConsoleServices.Output.WriteMarkupLine($"[grey]Context JSON: {contextPath}[/]");
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Translation pipeline execution failed.");
            ConsoleServices.Output.WriteError(ex.Message);
            return 1;
        }
    }

    /// <summary>读取目标语言台本：每非空行视为一句目标语言译文。</summary>
    /// <param name="path">台本文件路径；为空或不存在时返回空列表。</param>
    /// <param name="logger">记录读取告警的日志器。</param>
    /// <returns>台本行列表。</returns>
    private static List<string> LoadScriptLines(string? path, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(path))
            return [];

        if (!File.Exists(path))
        {
            logger.LogWarning("Target script file not found: {Path}", path);
            return [];
        }

        return File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();
    }
}
