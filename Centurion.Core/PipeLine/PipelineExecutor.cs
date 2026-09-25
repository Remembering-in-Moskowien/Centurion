using Centurion.Models.Console;

using System.Diagnostics;
using Centurion.Abstractions;
using Centurion.Abstractions.Pipeline;
using Centurion.Core.Infrastructure;
using Centurion.Models;
using Centurion.Models.Workflow;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Pipeline;

/// <summary>
/// Generic pipeline executor that runs a dynamic list of operators.
/// The caller provides the operator sequence; this class handles execution,
/// timing, health checks, and logging.
/// </summary>
public class PipelineExecutor
{
    private readonly ILogger<PipelineExecutor> _logger;

    /// <summary>
    /// 创建管线执行器实例。
    /// </summary>
    /// <param name="logger">用于记录执行过程的日志器。</param>
    public PipelineExecutor(ILogger<PipelineExecutor> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Executes the given sequence of pipeline operators.
    /// </summary>
    /// <param name="operators">The ordered list of operators to execute.</param>
    /// <param name="context">Workflow context containing configuration and state.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A dictionary mapping operator names to their execution duration.</returns>
    public async Task ExecuteAsync(IEnumerable<IPipelineOperator> operators,
        SubtitleWorkflowContext context,
        CancellationToken cancellationToken)
    {
        if (operators == null)
            throw new ArgumentNullException(nameof(operators));

        var stepTimings = new Dictionary<string, TimeSpan>();
        context.State.Extensions["StepTimings"] = stepTimings;

        // 每步执行反馈输出到控制台（精简：一步一行，避免刷屏）；细节走 ILogger（--verbose 可见）
        ConsoleServices.Output.WriteLine(ConsoleServices.T("Pipeline execution started"));
        _logger.LogInformation("Pipeline started for {InputPath}", context.Config.InputFilePath);

        var totalStopwatch = Stopwatch.StartNew();

        foreach (var op in operators)
        {
            // Health check (if supported)
            if (op is IHealthCheckableOperator healthy)
            {
                await healthy.CheckHealthAsync(cancellationToken);
            }

            var stepName = op.Name;
            var stepStopwatch = Stopwatch.StartNew();

            ConsoleServices.Output.WriteLine(ConsoleServices.T("Executing step: {0}", stepName));
            _logger.LogInformation("Starting step: {StepName}", stepName);

            await op.ExecuteAsync(context, cancellationToken);

            stepStopwatch.Stop();
            var elapsed = stepStopwatch.Elapsed;
            stepTimings[stepName] = elapsed;

            _logger.LogInformation(@"Step '{StepName}' completed in {Elapsed:mm\:ss\.fff}", stepName, elapsed);
        }

        totalStopwatch.Stop();
        var totalElapsed = totalStopwatch.Elapsed;
        ConsoleServices.Output.WriteLine(ConsoleServices.T("Total pipeline time: {0}", $@"{totalElapsed:mm\:ss\.fff}"));
        _logger.LogInformation(@"Total pipeline execution time: {Total:mm\:ss\.fff}", totalElapsed);
    }
}
