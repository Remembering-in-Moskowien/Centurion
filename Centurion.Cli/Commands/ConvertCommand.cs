using Centurion.Models.Console;
using Centurion.Core.Utils;
using Centurion.Cli.Commands.Settings;
using Centurion.Abstractions;
using Centurion.Abstractions.Pipeline;
using Centurion.Abstractions.Utils;
using Centurion.Core.Infrastructure;
using Centurion.Models.Ass;
using Centurion.Models.Workflow;
using Centurion.Core.Pipeline;
using Centurion.Core.Pipeline.Operators;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;

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

    /// <summary>
    /// 使用管道执行器与转换算子工厂初始化命令。
    /// </summary>
    /// <param name="executor">负责按顺序执行算子管道的执行器。</param>
    /// <param name="convertOperatorsFactory">创建转换管道所需算子集合的工厂委托。</param>
    /// <param name="qualityReportOp">质量报告算子（管线末尾写出 .quality.json）。</param>
    /// <param name="logger">记录命令执行失败的日志器。</param>
    public ConvertCommand(
        PipelineExecutor executor,
        Func<IEnumerable<IPipelineOperator>> convertOperatorsFactory,
        QualityReportOperator qualityReportOp,
        ILogger<ConvertCommand> logger)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _convertOperatorsFactory = convertOperatorsFactory ?? throw new ArgumentNullException(nameof(convertOperatorsFactory));
        _qualityReportOp = qualityReportOp ?? throw new ArgumentNullException(nameof(qualityReportOp));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

            // 执行管道（仅包含解析算子）
            var operators = _convertOperatorsFactory().ToList(); // 返回 [ConvertParseOperator]
            operators.Add(_qualityReportOp);
            await _executor.ExecuteAsync(operators, workflowContext, cancellationToken);

            // 保存为 Centurion 中间文件（供后续命令继续处理）
            await CenturionFileIO.SaveAsync(workflowContext, outputPath, "convert", cancellationToken);

            ConsoleServices.Output.WriteSuccess(ConsoleServices.T("Conversion succeeded: {0}", outputPath));
            ConsoleServices.Output.WriteInfo(ConsoleServices.T("Build subtitles with: {0}", "Centurion build <file>.centurion.json"));
            return 0;
        }
        catch (Exception ex)
        {
            // 命令层为执行路径的最外层：此处统一输出唯一一次 fail
            FailLogGate.Log(_logger, ex, "Conversion pipeline execution failed.");
            return 1;
        }
    }
}
