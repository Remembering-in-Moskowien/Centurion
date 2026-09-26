using Centurion.Abstractions;
using Centurion.Abstractions.Pipeline;
using Centurion.Abstractions.Utils;
using Centurion.Cli.Commands.Settings;
using Centurion.Core.Capabilities.Infrastructure;using Centurion.Core.Workflow.Pipeline;using Centurion.Models.Workflow;
using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;
using Centurion.Core.Utils.Serialization;
using Centurion.Models.Console;
namespace Centurion.Cli.Commands;

/// <summary>
/// 独立算子小命令的公共基类：把单个（或最小编排的）管道算子暴露为独立 CLI 命令。
/// 输入统一为 Centurion 中间文件（源头命令如 transcribe/vocalsep 也接受媒体文件），
/// 输出统一为中间文件——与 convert 入口 / build 出口的架构保持一致。
/// 子类只需声明算子集合与媒体输入开关；加载、临时目录、执行与保存由本基类完成。
/// spawn 等打包命令保持不变。
/// </summary>
public abstract class OperatorCommandBase(
    ITempDirectoryManager tempManager,
    PipelineExecutor pipelineExecutor,
    ILogger logger, ICenturionDocumentStore store) : AsyncCommand<OperatorSettings>
{
    /// <summary>命令名（用于默认输出后缀与 meta 记录）。</summary>
    protected abstract string OpName { get; }

    /// <summary>是否接受媒体文件作为输入（源头命令为 true；纯中间文件命令为 false）。</summary>
    protected abstract bool AcceptsMedia { get; }

    /// <summary>按工作流配置组装算子；策略命令应在此阶段完成策略解析。</summary>
    protected abstract IEnumerable<IPipelineOperator> CreateOperators(WorkflowConfig config);

    /// <summary>
    /// 媒体输入时初始化工作流配置的钩子：子类可覆盖以设置语言、设备、开关等。
    /// </summary>
    protected virtual void ApplyMediaConfig(WorkflowConfig config, OperatorSettings settings)
    {
    }

    /// <summary>执行：解析输入 → 组装算子 → 执行 → 保存中间文件。</summary>
    protected override async Task<int> ExecuteAsync(CommandContext context, OperatorSettings settings, CancellationToken ct)
    {
        try
        {
            var inputPath = settings.InputFile.FullName;
            if (!File.Exists(inputPath))
                throw new FileNotFoundException($"Input file not found: {inputPath}", inputPath);

            var outputPath = settings.OutputFile?.FullName
                ?? CenturionFileIO.DefaultOutputPath(inputPath, OpName);

            // 输入解析：中间文件直接加载；媒体文件为源头命令新建上下文
            SubtitleWorkflowContext workflowContext;
            if (CenturionFileIO.IsCenturionFile(inputPath))
            {
                var loadedDoc = await store.LoadAsync(inputPath, ct);
                workflowContext = new SubtitleWorkflowContext(loadedDoc.Config) { State = loadedDoc.State };
            }
            else
            {
                if (!AcceptsMedia)
                    throw new InvalidDataException(
                        $"'{inputPath}' is not a Centurion intermediate file. Run 'Centurion convert <file>' first.");

                var config = new WorkflowConfig
                {
                    CommandName = OpName,
                    InputFilePath = inputPath,
                    OutputFilePath = outputPath,
                    Language = settings.Language,
                    Device = settings.Device
                };
                ApplyMediaConfig(config, settings);
                workflowContext = new SubtitleWorkflowContext(config);
            }

            workflowContext.Config.OutputFilePath = outputPath;

            var operators = CreateOperators(workflowContext.Config).ToList();

            await using var tempDir = await tempManager.CreateTempDirectoryAsync($"{OpName}_");
            workflowContext.State.PipelineTempDirectory = tempDir.Path;

            await pipelineExecutor.ExecuteAsync(operators, workflowContext, ct);

            var outDoc = CenturionDocumentBuilder.Create(workflowContext, OpName, outputPath);
            await store.SaveAsync(outDoc, outputPath, ct);

            ConsoleServices.Output.WriteSuccess(ConsoleServices.T("{0} completed: {1}", OpName, outputPath));
            ConsoleServices.Output.WriteInfo(ConsoleServices.T(
                "Continue with: {0}", $"Centurion {NextStepHint(workflowContext, OpName)} <{Path.GetFileName(outputPath)}>"));
            return 0;
        }
        catch (Exception ex)
        {
            FailLogGate.Log(logger, ex, $"{OpName} operator execution failed.");
            return 1;
        }
    }

    /// <summary>给出下一步建议命令名（按状态推进）。</summary>
    /// <param name="workflowContext">执行后的工作流上下文。</param>
    /// <param name="opName">本次执行的命令名（默认输出后缀）。</param>
    /// <returns>建议的后续命令名。</returns>
    private static string NextStepHint(SubtitleWorkflowContext workflowContext, string opName)
    {
        var state = workflowContext.State;
        if (state.IsTranscribed && !state.IsSplit && !state.IsAligned)
            return "split";
        if (state.IsSplit && !state.IsAligned)
            return "align";
        if (state.IsAligned || state.IsTranscribed)
            return "build";
        return opName;
    }
}
