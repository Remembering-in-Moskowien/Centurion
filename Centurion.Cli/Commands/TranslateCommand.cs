using Centurion.Cli.Commands.Settings;
using Centurion.Abstractions.Pipeline;
using Centurion.Core.Capabilities.Infrastructure;
using Centurion.Core.Workflow.Pipeline;using Centurion.Abstractions;
using Centurion.Abstractions.Factories;
using Centurion.Abstractions.Strategy;
using Centurion.Core.Workflow.Pipeline.Operators;using Centurion.Models.Ass;
using Centurion.Models.Workflow;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;
using Centurion.Abstractions.Utils;
using Centurion.Core.Utils.Parsing;
using Centurion.Core.Utils.Serialization;
using Centurion.Models.Console;
namespace Centurion.Cli.Commands;

/// <summary>
/// Translation: translate a Centurion intermediate file into a target language.
/// Only performs text-level translation alignment — the timeline and word details are kept unchanged.
/// Supports LLM strategy, a glossary file, and a target-language script (1:1 alignment when counts match).
/// </summary>
public sealed class TranslateCommand(
    ITranslationStrategyFactory strategyFactory,
    QualityReportOperator qualityReportOp,
    PipelineExecutor pipelineExecutor,
    IServiceProvider serviceProvider,
    ILogger<TranslateCommand> logger, ICenturionDocumentStore store) : AsyncCommand<TranslateSettings>
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

            var loadedDoc = await store.LoadAsync(inputPath, ct);
            var workflowContext = new SubtitleWorkflowContext(loadedDoc.Config) { State = loadedDoc.State };

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

            // 3) 创建翻译算子并走 DAG 管线（Translation → Quality Report；
            //    策略内部按批并行调用 LLM，台本行数一致时 1:1 对齐采用）
            var options = new TranslationOptions
            {
                SourceLanguage = settings.SourceLanguage ?? "auto",
                TargetLanguage = settings.TargetLanguage,
                Glossary = glossary,
                TargetScriptLines = targetScriptLines
            };
            var strategy = strategyFactory.Create(settings.Strategy, new Centurion.Models.Llm.LlmOptions
            {
                Model = settings.Model,
                ApiKey = settings.ApiKey,
                ProviderName = settings.LlmProvider,
                BaseUrl = settings.LlmBaseUrl
            });
            logger.LogInformation("Using translation strategy: {Strategy}", strategy.StrategyName);
            var translationOp = ActivatorUtilities.CreateInstance<TranslationOperator>(
                serviceProvider, strategy, options);
            var dag = BuildTranslateDag(translationOp, qualityReportOp);

            // --dry-run：预览 DAG / 模型 / 成本，不执行
            if (settings.DryRun)
                return await DryRunHelper.PreviewAsync(dag, workflowContext.Config, serviceProvider, settings.Json, ct);

            var stepResults = await pipelineExecutor.ExecuteAsync(dag, workflowContext, ct);
            var skipped = stepResults.Where(r => r.Status == PipelineStepStatus.Skipped).Select(r => r.Name).ToList();
            if (skipped.Count > 0)
                logger.LogInformation("Skipped {Count} conditional step(s): {Names}", skipped.Count, string.Join(", ", skipped));

            // 5) 保存翻译后的中间文件（译文写入各句 TranslatedText，时间轴保持不变）
            var outDoc = CenturionDocumentBuilder.Create(workflowContext, "translate", outputPath);
            await store.SaveAsync(outDoc, outputPath, ct);

            var translatedCount = sentences.Count(s => !string.IsNullOrWhiteSpace(s.TranslatedText));
            ConsoleServices.Output.WriteSuccess(ConsoleServices.T("Translation completed"));
            ConsoleServices.Output.WriteInfo(ConsoleServices.T("Translated {0}/{1} sentences -> {2}", translatedCount, sentences.Count, settings.TargetLanguage));
            ConsoleServices.Output.WriteInfo(ConsoleServices.T("Build subtitles with: {0}", "Centurion build <file>.centurion.json"));
            if (settings.Json)
            {
                JsonOutput.Write(new
                {
                    command = "translate",
                    status = "ok",
                    input = inputPath,
                    output = outputPath,
                    translated = translatedCount,
                    total = sentences.Count,
                    steps = workflowContext.State.StepTimings?.Select(kv => new { name = kv.Key, elapsedSeconds = kv.Value.TotalSeconds })
                });
            }
            return ExitCodes.Success;
        }
        catch (Exception ex)
        {
            CliErrorPrinter.Print(logger, ex, "Translation pipeline execution failed.");
            return ExitCodes.Failure;
        }
    }

    /// <summary>
    /// 组装 translate DAG（pipeline graph 命令与 translate 命令共享的单一事实源）。
    /// </summary>
    internal static PipelineDag BuildTranslateDag(
        TranslationOperator translationOp,
        QualityReportOperator qualityReportOp)
    {
        var builder = PipelineDag.CreateBuilder();
        builder
            .Add("Translation", translationOp, description: "LLM 分批并行翻译（含术语表/台本约束）")
            .Add("Quality Report", qualityReportOp, dependsOn: ["Translation"], description: "翻译质量报告");
        return builder.Build();
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
