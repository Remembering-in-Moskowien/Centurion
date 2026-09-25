using Centurion.Abstractions.Pipeline;
using Centurion.Core.Utils;
using Centurion.Models.Workflow;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using System.Diagnostics;

namespace Centurion.Core.Pipeline.Operators;

/// <summary>
/// 质量报告算子：在所有处理完成后，从工作流上下文提取指标并写出 .quality.json。
/// 各路径（spawn/from-script/correct/convert/translate/dub）在管线末尾统一追加本算子。
/// </summary>
public sealed class QualityReportOperator(ILogger<QualityReportOperator> logger)
    : PipelineOperatorBase<QualityReportOperator>(logger)
{
    private static readonly JsonSerializerSettings Settings = new()
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
        File.WriteAllText(reportPath, JsonConvert.SerializeObject(report, Settings));

        context.State.Extensions["QualityReportPath"] = reportPath;

        LogInfo($"Quality report written to '{reportPath}' (sentences: {report.Counts.SentenceCount}, speakers: {report.Counts.SpeakerCount}).");
        return Task.CompletedTask;
    }
}
