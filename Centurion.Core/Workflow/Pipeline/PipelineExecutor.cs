
using System.Diagnostics;
using Centurion.Abstractions;
using Centurion.Abstractions.Pipeline;
using Centurion.Core.Capabilities.Infrastructure;
using Centurion.Models;
using Centurion.Models.Workflow;
using Microsoft.Extensions.Logging;
using Centurion.Models.Console;
namespace Centurion.Core.Workflow.Pipeline;

/// <summary>
/// DAG 管线执行器：节点为算子、边为数据依赖。
/// - 拓扑调度：依赖全部完成的节点并行执行（无依赖冲突即并发）。
/// - 条件节点：When 谓词为 false 时跳过（Skipped，不记失败）。
/// - 每节点支持：超时、失败重试（指数退避）、取消、降级（重试耗尽后跳过继续）。
/// - 兼容旧接口：ExecuteAsync(IEnumerable&lt;IPipelineOperator&gt;) 自动构造成链式 DAG，行为与串行一致。
/// </summary>
public sealed class PipelineExecutor
{
    private readonly ILogger<PipelineExecutor> _logger;

    /// <summary>节点级重试退避基数（指数退避：base * 2^(attempt-1)）。</summary>
    private static readonly TimeSpan RetryBaseDelay = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// 创建管线执行器实例。
    /// </summary>
    /// <param name="logger">用于记录执行过程的日志器。</param>
    public PipelineExecutor(ILogger<PipelineExecutor> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 以链式 DAG 执行给定线性算子列表（保持原串行语义与退出行为）。
    /// </summary>
    public async Task ExecuteAsync(IEnumerable<IPipelineOperator> operators,
        SubtitleWorkflowContext context,
        CancellationToken cancellationToken)
    {
        if (operators == null)
            throw new ArgumentNullException(nameof(operators));

        var dag = PipelineDag.FromSequence(operators);
        await ExecuteGraphAsync(dag, context, cancellationToken);
    }

    /// <summary>
    /// 执行 DAG 管线。所有节点成功完成返回；存在不可降级失败时抛出 <see cref="PipelineExecutionException"/>。
    /// 节点执行结果写入 <paramref name="context"/> 的 StepTimings 及本方法返回值。
    /// </summary>
    /// <returns>每个节点的执行结果（状态/耗时/重试次数）。</returns>
    public async Task<IReadOnlyList<PipelineStepResult>> ExecuteAsync(PipelineDag dag,
        SubtitleWorkflowContext context,
        CancellationToken cancellationToken)
        => await ExecuteGraphAsync(dag, context, cancellationToken);

    private async Task<IReadOnlyList<PipelineStepResult>> ExecuteGraphAsync(
        PipelineDag dag,
        SubtitleWorkflowContext context,
        CancellationToken cancellationToken)
    {
        if (dag == null)
            throw new ArgumentNullException(nameof(dag));
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        var validationError = dag.Validate();
        if (validationError is not null)
            throw new InvalidOperationException(validationError);

        var stepTimings = new Dictionary<string, TimeSpan>();
        context.State.StepTimings = stepTimings;

        ConsoleServices.Output.WriteLine(ConsoleServices.T("Pipeline execution started"));
        _logger.LogInformation("Pipeline started for {InputPath}", context.Config.InputFilePath);

        var totalStopwatch = Stopwatch.StartNew();
        var results = await ExecuteReadyNodesAsync(dag, context, cancellationToken, stepTimings);
        totalStopwatch.Stop();

        ConsoleServices.Output.WriteLine(ConsoleServices.T("Total pipeline time: {0}", $@"{totalStopwatch.Elapsed:mm\:ss\.fff}"));
        _logger.LogInformation(@"Total pipeline execution time: {Total:mm\:ss\.fff}", totalStopwatch.Elapsed);
        return results;
    }

    /// <summary>
    /// 拓扑调度：维护就绪节点集合，就绪节点并行执行；依赖完成后唤醒下游。
    /// </summary>
    private async Task<IReadOnlyList<PipelineStepResult>> ExecuteReadyNodesAsync(
        PipelineDag dag,
        SubtitleWorkflowContext context,
        CancellationToken cancellationToken,
        Dictionary<string, TimeSpan> stepTimings)
    {
        var nodes = dag.Nodes;
        var byName = nodes.ToDictionary(n => n.Name, StringComparer.Ordinal);

        // 依赖完成集合（含被跳过/降级的节点——跳过同样视为"依赖满足"）
        var completed = new HashSet<string>(StringComparer.Ordinal);
        // 每节点剩余依赖计数
        var remaining = nodes.ToDictionary(n => n.Name, n => n.DependsOn.Count, StringComparer.Ordinal);
        var ready = new Queue<PipelineNode>(nodes.Where(n => n.DependsOn.Count == 0));
        var results = new List<PipelineStepResult>(nodes.Count);
        var resultByNode = new Dictionary<string, PipelineStepResult>(StringComparer.Ordinal);

        // 就绪节点的并行执行以 Task 形式运行；用信号量天然限流（不设上限即全并行）
        while (ready.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var wave = new List<PipelineNode>();
            while (ready.Count > 0)
                wave.Add(ready.Dequeue());

            var waveResults = await Task.WhenAll(
                wave.Select(node => ExecuteNodeWithPolicyAsync(node, context, cancellationToken)));

            foreach (var result in waveResults)
            {
                results.Add(result);
                resultByNode[result.Name] = result;
                completed.Add(result.Name);
                stepTimings[result.Name] = result.Elapsed;

                if (result.Status == PipelineStepStatus.Failed)
                    throw new PipelineExecutionException(
                        $"Pipeline node '{result.Name}' failed and is not degradable.", result.Name, result.Error);

                // 唤醒依赖此节点的下游
                foreach (var node in nodes)
                {
                    if (node.DependsOn.Contains(result.Name, StringComparer.Ordinal)
                        && --remaining[node.Name] == 0)
                        ready.Enqueue(node);
                }
            }
        }

        if (completed.Count != nodes.Count)
        {
            var orphaned = nodes.Where(n => !completed.Contains(n.Name)).Select(n => n.Name).ToList();
            throw new PipelineExecutionException(
                $"Pipeline terminated with unreachable nodes: {string.Join(", ", orphaned)}.",
                "pipeline", null);
        }

        return results;
    }

    /// <summary>
    /// 单节点执行策略：健康检查 → 条件判定 → 带超时执行 → 失败重试（指数退避）→ 降级/失败。
    /// </summary>
    private async Task<PipelineStepResult> ExecuteNodeWithPolicyAsync(
        PipelineNode node,
        SubtitleWorkflowContext context,
        CancellationToken cancellationToken)
    {
        var nodeName = node.Name;
        var stopwatch = Stopwatch.StartNew();
        var attempts = 0;

        // 条件节点：不满足则跳过（不消耗重试，不计失败）
        if (node.When is not null)
        {
            try
            {
                if (!node.When(context))
                {
                    stopwatch.Stop();
                    _logger.LogInformation("Node '{Node}' skipped by condition.", nodeName);
                    return new PipelineStepResult
                    {
                        Name = nodeName,
                        Status = PipelineStepStatus.Skipped,
                        Elapsed = stopwatch.Elapsed,
                        Attempts = 0,
                        SkipReason = "Condition not satisfied."
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Node '{Node}' condition evaluation failed; treating as skip.", nodeName);
                stopwatch.Stop();
                return new PipelineStepResult
                {
                    Name = nodeName,
                    Status = PipelineStepStatus.Skipped,
                    Elapsed = stopwatch.Elapsed,
                    Attempts = 0,
                    SkipReason = $"Condition evaluation failed: {ex.Message}"
                };
            }
        }

        // 健康检查（如支持）：失败即抛——环境/模型缺失是硬错误（如模型未安装），
        // 不重试、不降级，直接终止任务并向上传播（命令层报错退出并提示安装）
        try
        {
            if (node.Operator is IHealthCheckableOperator healthy)
                await healthy.CheckHealthAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Health check failed for node '{Node}' ({Message}).", nodeName, ex.Message);
            throw;
        }

        ConsoleServices.Output.WriteLine(ConsoleServices.T("Executing step: {0}", nodeName));
        _logger.LogInformation("Starting step: {StepName}", nodeName);

        Exception? lastError = null;
        var maxAttempts = Math.Max(1, node.MaxRetries + 1);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            attempts = attempt;
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await ExecuteWithTimeoutAsync(node, context, cancellationToken);

                stopwatch.Stop();
                var status = attempt > 1 ? PipelineStepStatus.Retried : PipelineStepStatus.Completed;
                _logger.LogInformation(@"Step '{StepName}' {Status} in {Elapsed:mm\:ss\.fff} (attempt {Attempt})",
                    nodeName, status, stopwatch.Elapsed, attempt);
                return new PipelineStepResult
                {
                    Name = nodeName,
                    Status = status,
                    Elapsed = stopwatch.Elapsed,
                    Attempts = attempt
                };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = ex;
                _logger.LogWarning(ex, "Step '{StepName}' failed on attempt {Attempt}/{MaxAttempts}.",
                    nodeName, attempt, maxAttempts);
                if (attempt < maxAttempts)
                    await Task.Delay(RetryBackoff(attempt), cancellationToken);
            }
        }

        stopwatch.Stop();

        // 重试耗尽：可降级则跳过继续，否则失败终止任务
        if (node.DegradeOnFailure)
        {
            _logger.LogWarning("Step '{StepName}' exhausted retries ({Attempts}); degrading (skipping).",
                nodeName, maxAttempts);
            return new PipelineStepResult
            {
                Name = nodeName,
                Status = PipelineStepStatus.Degraded,
                Elapsed = stopwatch.Elapsed,
                Attempts = attempts,
                Error = lastError
            };
        }

        return new PipelineStepResult
        {
            Name = nodeName,
            Status = PipelineStepStatus.Failed,
            Elapsed = stopwatch.Elapsed,
            Attempts = attempts,
            Error = lastError
        };
    }

    /// <summary>节点级超时执行：超过 Timeout 的尝试视为失败（以 TimeoutException 形式抛出）。</summary>
    private static async Task ExecuteWithTimeoutAsync(
        PipelineNode node,
        SubtitleWorkflowContext context,
        CancellationToken cancellationToken)
    {
        if (node.Timeout is null)
        {
            await node.Operator.ExecuteAsync(context, cancellationToken);
            return;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(node.Timeout.Value);
        try
        {
            await node.Operator.ExecuteAsync(context, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Step '{node.Name}' timed out after {node.Timeout.Value.TotalSeconds:F1}s.");
        }
    }

    private static TimeSpan RetryBackoff(int attempt) => RetryBaseDelay * Math.Pow(2, Math.Clamp(attempt - 1, 0, 5));
}

/// <summary>管线执行失败（不可降级节点失败）时抛出。</summary>
public sealed class PipelineExecutionException(
    string message,
    string nodeName,
    Exception? innerException) : InvalidOperationException(message, innerException)
{
    /// <summary>失败节点名称。</summary>
    public string NodeName { get; } = nodeName;
}
