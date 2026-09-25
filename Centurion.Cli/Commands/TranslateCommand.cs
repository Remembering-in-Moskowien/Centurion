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
using Centurion.Abstractions.Utils;

namespace Centurion.Cli.Commands;

/// <summary>
/// Translation: translate a Centurion intermediate file into a target language.
/// Only performs text-level translation alignment — the timeline and word details are kept unchanged.
/// Supports LLM strategy, a glossary file, and a target-language script (1:1 alignment when counts match).
/// </summary>
public sealed class TranslateCommand(
    ITranslationStrategyFactory strategyFactory,
    QualityReportOperator qualityReportOp,
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
            var inputPath = settings.CenturionFile.FullName;
            if (!File.Exists(inputPath))
                throw new FileNotFoundException($"Centurion intermediate file not found: {inputPath}", inputPath);
            if (string.IsNullOrWhiteSpace(settings.TargetLanguage))
                throw new ArgumentException("--target-language is required.");

            var outputPath = settings.OutputFile?.FullName ??
                             CenturionFileIO.DefaultOutputPath(inputPath, "translated");

            var workflowContext = await CenturionFileIO.LoadAsync(inputPath, ct);

            // 更新配置：翻译相关字段
            workflowContext.Config = new WorkflowConfig
            {
                CommandName = "translate",
                InputFilePath = workflowContext.Config.InputFilePath ?? inputPath,
                SubtitleFilePath = workflowContext.Config.SubtitleFilePath ?? inputPath,
                OutputFilePath = outputPath,
                Language = settings.SourceLanguage ?? "auto",
                TargetLanguage = settings.TargetLanguage,
                TranslationStrategy = settings.Strategy,
                TranslationModel = settings.Model,
                TranslationApiKey = settings.ApiKey,
                TranslationProvider = settings.LlmProvider,
                TranslationBaseUrl = settings.LlmBaseUrl,
                GlossaryPath = settings.Glossary?.FullName,
                TargetScriptPath = settings.TargetScript?.FullName,
                Bilingual = settings.Bilingual,
                KaraokeMode = settings.Karaoke,
                ShowSpeakerLabels = workflowContext.Config.ShowSpeakerLabels,
                CacheDirectory = workflowContext.Config.CacheDirectory ?? "./cache"
            };

            var sentences = workflowContext.State.CurrentSentences;

            // 2) 加载术语表与目标语言台本
            var glossary = GlossaryLoader.Load(settings.Glossary?.FullName, logger);
            var targetScriptLines = LoadScriptLines(settings.TargetScript?.FullName, logger);

            // 3) 创建翻译策略并执行（LLM 分批翻译；台本行数一致时 1:1 对齐采用）
            var strategy = strategyFactory.Create(settings.Strategy, new Centurion.Models.Llm.LlmOptions
            {
                Model = settings.Model,
                ApiKey = settings.ApiKey,
                ProviderName = settings.LlmProvider,
                BaseUrl = settings.LlmBaseUrl
            });
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

            // 4) 质量报告（句子数、翻译覆盖率、时间轴统计）
            await qualityReportOp.ExecuteAsync(workflowContext, ct);

            // 5) 保存翻译后的中间文件（译文写入各句 TranslatedText，时间轴保持不变）
            await CenturionFileIO.SaveAsync(workflowContext, outputPath, "translate", ct);

            var translatedCount = sentences.Count(s => !string.IsNullOrWhiteSpace(s.TranslatedText));
            ConsoleServices.Output.WriteSuccess(ConsoleServices.T("Translation completed: {0}", outputPath));
            ConsoleServices.Output.WriteInfo(ConsoleServices.T("Translated {0}/{1} sentences -> {2}", translatedCount, sentences.Count, settings.TargetLanguage));
            ConsoleServices.Output.WriteInfo(ConsoleServices.T("Build subtitles with: {0}", "Centurion build <file>.centurion.json"));
            return 0;
        }
        catch (Exception ex)
        {
            FailLogGate.Log(logger, ex, "Translation pipeline execution failed.");
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
