
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
/// DAG pipeline executor: nodes are operators, edges are data dependencies.
/// - Topological scheduling: nodes whose dependencies have all completed run in parallel (concurrent whenever dependencies do not conflict).
/// - Conditional nodes: skipped (Skipped, not counted as a failure) when the When predicate is false.
/// - Per node supports: timeout, retry on failure (exponential backoff), cancellation, degradation (skip and continue once retries are exhausted).
/// - Back-compat: ExecuteAsync(IEnumerable&lt;IPipelineOperator&gt;) automatically builds a chained DAG, behaving identically to sequential execution.
/// </summary>
public sealed class PipelineExecutor
{
    private readonly ILogger<PipelineExecutor> _logger;

    /// <summary>Per-node retry backoff base (exponential backoff: base * 2^(attempt-1)).</summary>
    private static readonly TimeSpan RetryBaseDelay = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// Creates a pipeline executor instance.
    /// </summary>
    /// <param name="logger">Logger used to record the execution process.</param>
    public PipelineExecutor(ILogger<PipelineExecutor> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Executes the given linear operator list as a chained DAG (preserving the original sequential semantics and exit behavior).
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
    /// Executes the DAG pipeline. Returns when all nodes complete successfully; throws <see cref="PipelineExecutionException"/>
    /// when a non-degradable failure occurs. Per-node execution results are written to <paramref name="context"/>'s
    /// StepTimings and returned by this method.
    /// </summary>
    /// <returns>The execution result of each node (status/elapsed/attempts).</returns>
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

        ConsoleServices.Output.WriteMarkupLine($"[bold cyan]{CliSymbols.Play}[/] {ConsoleServices.T("Pipeline execution started")}");
        _logger.LogInformation("Pipeline started for {InputPath}", context.Config.InputFilePath);

        var totalStopwatch = Stopwatch.StartNew();
        var results = await ExecuteReadyNodesAsync(dag, context, cancellationToken, stepTimings);
        totalStopwatch.Stop();

        ConsoleServices.Output.WriteMarkupLine($"[bold green]{CliSymbols.Check}[/] {ConsoleServices.T("Total pipeline time: {0}", $@"{totalStopwatch.Elapsed:mm\:ss\.fff}")}");
        _logger.LogInformation(@"Total pipeline execution time: {Total:mm\:ss\.fff}", totalStopwatch.Elapsed);
        return results;
    }

    /// <summary>
    /// Topological scheduling: maintains the set of ready nodes, runs ready nodes in parallel, and wakes downstream nodes once dependencies complete.
    /// </summary>
    private async Task<IReadOnlyList<PipelineStepResult>> ExecuteReadyNodesAsync(
        PipelineDag dag,
        SubtitleWorkflowContext context,
        CancellationToken cancellationToken,
        Dictionary<string, TimeSpan> stepTimings)
    {
        var nodes = dag.Nodes;
        var byName = nodes.ToDictionary(n => n.Name, StringComparer.Ordinal);

        // Completed-dependencies set (includes skipped/degraded nodes -- a skip also counts as "dependencies satisfied")
        var completed = new HashSet<string>(StringComparer.Ordinal);
        // Remaining dependency count per node
        var remaining = nodes.ToDictionary(n => n.Name, n => n.DependsOn.Count, StringComparer.Ordinal);
        var ready = new Queue<PipelineNode>(nodes.Where(n => n.DependsOn.Count == 0));
        var results = new List<PipelineStepResult>(nodes.Count);
        var resultByNode = new Dictionary<string, PipelineStepResult>(StringComparer.Ordinal);

        // Ready nodes run in parallel as Tasks; a semaphore naturally throttles them (no cap = fully parallel)
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

                if (result.Status is PipelineStepStatus.Completed or PipelineStepStatus.Retried)
                    ConsoleServices.Output.WriteMarkupLine(
                        $"[dim]    {CliSymbols.Done} {result.Name} in {result.Elapsed.TotalSeconds:F1}s[/]");
                else if (result.Status == PipelineStepStatus.Skipped)
                    ConsoleServices.Output.WriteMarkupLine($"[dim]    - {result.Name} skipped[/]");

                if (result.Status == PipelineStepStatus.Failed)
                    throw new PipelineExecutionException(
                        $"Pipeline node '{result.Name}' failed and is not degradable.", result.Name, result.Error);

                // Wake downstream nodes that depend on this node
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
    /// Single-node execution policy: health check -> condition check -> timed execution -> retry on failure (exponential backoff) -> degrade/fail.
    /// </summary>
    private async Task<PipelineStepResult> ExecuteNodeWithPolicyAsync(
        PipelineNode node,
        SubtitleWorkflowContext context,
        CancellationToken cancellationToken)
    {
        var nodeName = node.Name;
        var stopwatch = Stopwatch.StartNew();
        var attempts = 0;

        // Conditional node: skipped when not satisfied (consumes no retries, not counted as a failure)
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

        // Health check (if supported): throws on failure -- a missing environment/model is a hard error (e.g. model
        // not installed); no retry, no degradation; terminate the task and propagate upward (the command layer exits with an error and prompts to install).
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

        ConsoleServices.Output.WriteMarkupLine($"[cyan]→[/] {ConsoleServices.T("Executing step: {0}", nodeName)}");
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

        // Retries exhausted: degrade by skipping and continuing if possible, otherwise fail and terminate the task
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

    /// <summary>Per-node timed execution: an attempt exceeding Timeout is treated as a failure (thrown as a TimeoutException).</summary>
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

/// <summary>Thrown when pipeline execution fails (a non-degradable node failed).</summary>
public sealed class PipelineExecutionException(
    string message,
    string nodeName,
    Exception? innerException) : InvalidOperationException(message, innerException)
{
    /// <summary>The name of the failed node.</summary>
    public string NodeName { get; } = nodeName;
}
