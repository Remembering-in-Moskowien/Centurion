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
/// <c>convert</c> command: parses existing subtitle files into Centurion intermediate
/// files (*.centurion.json). The IR is the single structured subtitle exchange format
/// in the chain, consumed by correct/translate/dub/build.
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
    /// Initializes the command with a pipeline executor and a convert-operator factory.
    /// </summary>
    /// <param name="executor">Executes the operator pipeline in order.</param>
    /// <param name="convertOperatorsFactory">Factory delegate building the operator set for the conversion pipeline.</param>
    /// <param name="qualityReportOp">Quality report operator (writes .quality.json at the pipeline tail).</param>
    /// <param name="logger">Logs command execution failures.</param>
    /// <param name="store">Intermediate file store (*.centurion.json read/write/validate/migrate).</param>
    /// <param name="serviceProvider">Service container (operator factories/strategy resolution).</param>
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
    /// Runs conversion: parses the input subtitle, runs the convert pipeline and writes the IR.
    /// </summary>
    /// <param name="context">The Spectre command context.</param>
    /// <param name="settings">The convert command settings.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
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
    /// Assembles the convert DAG (single source of truth shared with the pipeline graph command):
    /// input subtitle parse → quality report.
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
                description: "Parse the input subtitle (ASS/SRT/TXT) into structured sentences");
            previous = name;
        }
        if (previous is null)
            throw new ArgumentException("convert pipeline requires at least one parse operator.");

        builder.Add("Quality Report", qualityReportOp, dependsOn: [previous], description: "Quality report wrap-up");
        return builder.Build();
    }
}
