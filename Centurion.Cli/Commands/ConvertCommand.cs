// File: Centurion.Cli/Commands/ConvertCommand.cs

using Centurion.Cli.Commands.Settings;
using Centurion.Core;
using Centurion.Core.Abstractions;
using Centurion.Core.Models;
using Centurion.Core.PipeLine;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Centurion.Cli.Commands;

public sealed class ConvertCommand : AsyncCommand<ConvertSettings>
{
    private readonly PipelineExecutor _executor;
    private readonly Func<IEnumerable<IPipelineOperator>> _convertOperatorsFactory;

    public ConvertCommand(
        PipelineExecutor executor,
        Func<IEnumerable<IPipelineOperator>> convertOperatorsFactory)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _convertOperatorsFactory = convertOperatorsFactory ?? throw new ArgumentNullException(nameof(convertOperatorsFactory));
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, ConvertSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var config = new WorkflowConfig
            {
                InputFilePath = settings.InputFile.FullName,
                OutputFilePath = settings.OutputFile?.FullName
                    ?? Path.ChangeExtension(settings.InputFile.FullName, ".ass")
            };

            var workflowContext = new SubtitleWorkflowContext(config);

            // 执行管道（仅包含解析算子）
            var operators = _convertOperatorsFactory(); // 返回 [ConvertParseOp]
            await _executor.ExecuteAsync(operators, workflowContext, cancellationToken);

            // 使用 AssSubBuilder 从上下文构建 ASS 字幕
            var assBuilder = AssSubBuilder.FromWorkflow(workflowContext);
            var assDoc = assBuilder.Build();
            await File.WriteAllTextAsync(config.OutputFilePath, assDoc.ToString(), cancellationToken);

            AnsiConsole.MarkupLine($"[green]Conversion succeeded: {config.OutputFilePath}[/]");
            return 0;
        }
        catch (Exception ex)
        {
            ConsoleServices.Output.WriteError($"Error: {ex.Message}");
            return 1;
        }
    }
}