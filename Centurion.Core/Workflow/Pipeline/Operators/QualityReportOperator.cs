using Centurion.Abstractions.Pipeline;
using Centurion.Models.Providers;
using Centurion.Models.Workflow;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using System.Diagnostics;
using Centurion.Core.Utils.Reporting;
namespace Centurion.Core.Workflow.Pipeline.Operators;

/// <summary>
/// 质量报告算子：在所有处理完成后，从工作流上下文提取指标并写出 .quality.json。
/// 各路径（spawn/from-script/correct/convert/translate/dub）在管线末尾统一追加本算子。
/// </summary>
public sealed class QualityReportOperator(ILogger<QualityReportOperator> logger)
    : PipelineOperatorBase<QualityReportOperator>(logger)
{
    internal static readonly JsonSerializerSettings SerializerSettings = new()
    {
        Formatting = Formatting.Indented,
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        NullValueHandling = NullValueHandling.Ignore,
        Converters = [new StringEnumConverter()]
    };

    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    /// <summary>算子名称。</summary>
    public override string Name => "Quality Report";

    /// <summary>
    /// 构建质量报告并写出 .quality.json，路径与输出字幕同名。
    /// </summary>
    /// <param name="context">工作流上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public override Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken)
    {
        var outputPath = context.Config.OutputFilePath ?? Path.ChangeExtension(context.Config.InputFilePath, ".ass");
        var report = QualityReportBuilder.Build(context, outputPath, _stopwatch.Elapsed.TotalSeconds);

        var reportPath = Path.ChangeExtension(outputPath, ".quality.json");
        File.WriteAllText(reportPath, JsonConvert.SerializeObject(report, SerializerSettings));

        var htmlPath = Path.ChangeExtension(outputPath, ".quality.html");
        File.WriteAllText(htmlPath, QualityHtmlReport.Render(report));

        context.State.Extensions["QualityReportPath"] = reportPath;
        context.State.Extensions["QualityReportHtmlPath"] = htmlPath;

        LogProviderUsage(context);

        LogInfo($"Quality report written to '{reportPath}' (sentences: {report.Counts.SentenceCount}, speakers: {report.Counts.SpeakerCount}); HTML: '{htmlPath}'.");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 输出 Provider 用量汇总（token/音频分钟/缓存命中/估算成本）。
    /// 数据来自 <see cref="WorkflowState.ProviderUsages"/>（进程内聚合）。
    /// </summary>
    private static void LogProviderUsage(SubtitleWorkflowContext context)
    {
        var usages = context.State.ProviderUsages;
        if (usages.Count == 0)
            return;

        var tokensIn = usages.Sum(u => u.TokensIn);
        var tokensOut = usages.Sum(u => u.TokensOut);
        var audioMinutes = usages.Sum(u => u.AudioSeconds) / 60.0;
        var cacheHits = usages.Sum(u => u.CacheHits);
        var cost = usages.Sum(u => u.EstimatedCostUsd);
        var via = string.Join(", ", usages.Select(u => u.ProviderName).Distinct());
        Console.WriteLine(
            $"[provider] usage: tokens {tokensIn}+{tokensOut}, audio {audioMinutes:F2}m, cache hits {cacheHits}, est. cost ${cost:F4} (via {via})");
    }
}
