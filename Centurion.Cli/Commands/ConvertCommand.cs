using Centurion.Cli.Commands.Settings;
using Centurion.Abstractions;
using Centurion.Abstractions.Pipeline;
using Centurion.Abstractions.Utils;
using Centurion.Core.Capabilities.Infrastructure;using Centurion.Models.Ass;
using Centurion.Models.Workflow;
using Centurion.Core.Workflow.Pipeline;using Centurion.Core.Workflow.Pipeline.Operators;using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;
using Centurion.Core.Utils.Serialization;
using Centurion.Models.Console;
namespace Centurion.Cli.Commands;

/// <summary>
/// <c>convert</c> 命令：解析现有字幕文件并转换为 Centurion 中间文件（*.centurion.json）。
/// 中间文件是命令链中唯一的结构化字幕交换格式，供 correct/translate/dub/build 继续处理。
/// </summary>
public sealed class ConvertCommand : AsyncCommand<ConvertSettings>
{
    private readonly PipelineExecutor _executor;
    private readonly Func<IEnumerable<IPipelineOperator>> _convertOperatorsFactory;
    private readonly QualityReportOperator _qualityReportOp;
    private readonly ILogger<ConvertCommand> _logger;
    private readonly ICenturionDocumentStore _store;
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// 使用管道执行器与转换算子工厂初始化命令。
    /// </summary>
    /// <param name="executor">负责按顺序执行算子管道的执行器。</param>
    /// <param name="convertOperatorsFactory">创建转换管道所需算子集合的工厂委托。</param>
    /// <param name="qualityReportOp">质量报告算子（管线末尾写出 .quality.json）。</param>
    /// <param name="logger">记录命令执行失败的日志器。</param>
    /// <param name="store">中间文件存储（*.centurion.json 读写/校验/迁移）。</param>
    /// <param name="serviceProvider">服务容器（算子工厂/策略解析）。</param>
    public ConvertCommand(
        PipelineExecutor executor,
        Func<IEnumerable<IPipelineOperator>> convertOperatorsFactory,
        QualityReportOperator qualityReportOp,
        ILogger<ConvertCommand> logger,
        ICenturionDocumentStore store,
        IServiceProvider serviceProvider)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _convertOperatorsFactory = convertOperatorsFactory ?? throw new ArgumentNullException(nameof(convertOperatorsFactory));
        _qualityReportOp = qualityReportOp ?? throw new ArgumentNullException(nameof(qualityReportOp));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    /// <summary>
    /// 执行转换：解析输入字幕、运行转换算子管道并写出中间文件。
    /// </summary>
    /// <param name="context">Spectre 命令上下文。</param>
    /// <param name="settings">转换命令选项。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    protected override async Task<int> ExecuteAsync(CommandContext context, ConvertSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var outputPath = settings.OutputFile?.FullName
                ?? CenturionFileIO.DefaultOutputPath(settings.InputFile.FullName);
            var config = new WorkflowConfig
            {
                CommandName = "convert",
                InputFilePath = settings.InputFile.FullName,
                SubtitleFilePath = settings.InputFile.FullName,
                OutputFilePath = outputPath
            };

            var workflowContext = new SubtitleWorkflowContext(config);

            // convert DAG：解析算子 → 质量报告（pipeline-graph 命令共享同一装配）
            var dag = BuildConvertDag(_convertOperatorsFactory().ToList(), _qualityReportOp);

            // --dry-run：预览 DAG / 模型 / 成本，不执行
            if (settings.DryRun)
                return await DryRunHelper.PreviewAsync(dag, config, _serviceProvider, settings.Json, cancellationToken);

            await _executor.ExecuteAsync(dag, workflowContext, cancellationToken);

            // 保存为 Centurion 中间文件（供后续命令继续处理）
            var outDoc = CenturionDocumentBuilder.Create(workflowContext, "convert", outputPath);
            await _store.SaveAsync(outDoc, outputPath, cancellationToken);

            ConsoleServices.Output.WriteSuccess(ConsoleServices.T("Conversion succeeded"));
            ConsoleServices.Output.WriteInfo(ConsoleServices.T("Build subtitles with: {0}", "Centurion build <file>.centurion.json"));

            if (settings.Json)
            {
                JsonOutput.Write(new
                {
                    command = "convert",
                    status = "ok",
                    input = settings.InputFile.FullName,
                    output = outputPath,
                    steps = workflowContext.State.StepTimings?.Select(kv => new { name = kv.Key, elapsedSeconds = kv.Value.TotalSeconds })
                });
            }
            return ExitCodes.Success;
        }
        catch (Exception ex)
        {
            // 命令层为执行路径的最外层：此处统一输出唯一一次 fail
            CliErrorPrinter.Print(_logger, ex, "Conversion pipeline execution failed.");
            return ExitCodes.Failure;
        }
    }

    /// <summary>
    /// 组装 convert DAG（pipeline graph 命令与 convert 命令共享的单一事实源）：
    /// 输入字幕解析 → 质量报告。
    /// </summary>
    internal static PipelineDag BuildConvertDag(
        IReadOnlyList<Centurion.Abstractions.Pipeline.IPipelineOperator> parseOperators,
        QualityReportOperator qualityReportOp)
    {
        var builder = PipelineDag.CreateBuilder();
        var previous = (string?)null;
        var index = 0;
        foreach (var op in parseOperators)
        {
            var name = op.Name;
            while (builder.Contains(name))
                name = $"{op.Name}#{++index}";
            builder.Add(name, op, dependsOn: previous is null ? null : [previous],
                description: "解析输入字幕（ASS/SRT/TXT）为结构化句子");
            previous = name;
        }
        if (previous is null)
            throw new ArgumentException("convert pipeline requires at least one parse operator.");

        builder.Add("Quality Report", qualityReportOp, dependsOn: [previous], description: "质量报告收尾");
        return builder.Build();
    }
}
