using System.Diagnostics;
using Centurion.Abstractions.Pipeline;
using Centurion.Models.Workflow;
using Centurion.Core.Workflow.Pipeline;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Centurion.Tests.Core;

/// <summary>DAG 执行器：拓扑调度、并行、条件、重试、超时、降级与 IR 一致性测试。</summary>
public sealed class DagPipelineExecutorTests
{
    private static SubtitleWorkflowContext CreateContext(WorkflowConfig? config = null)
        => new(config ?? new WorkflowConfig { InputFilePath = "test.mp4" });

    private static PipelineExecutor CreateExecutor() => new(NullLogger<PipelineExecutor>.Instance);

    /// <summary>可编程 stub 算子：延迟、可选失败、可选写状态字段。</summary>
    private sealed class StubOperator(string name, TimeSpan? delay = null, int failTimes = 0, string? stateField = null, string? stateValue = null)
        : IPipelineOperator
    {
        public string Name { get; } = name;
        private int _calls;

        public async Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken = default)
        {
            var attempt = Interlocked.Increment(ref _calls);
            if (attempt <= failTimes)
                throw new InvalidOperationException($"{Name} injected failure (attempt {attempt}).");

            if (delay is not null)
                await Task.Delay(delay.Value, cancellationToken);

            if (stateField is not null)
                context.State.Extensions[$"{Name}:{stateField}"] = stateValue ?? Name;
        }
    }

    // ---------- 拓扑与依赖 ----------

    [Fact]
    public async Task Execute_LinearDag_ExecutesInDependencyOrder()
    {
        var order = new List<string>();
        var a = new OrderOperator("A", order);
        var b = new OrderOperator("B", order);
        var c = new OrderOperator("C", order);
        var dag = PipelineDag.CreateBuilder()
            .Add("A", a)
            .Add("B", b, dependsOn: ["A"])
            .Add("C", c, dependsOn: ["B"])
            .Build();

        var results = await CreateExecutor().ExecuteAsync(dag, CreateContext(), CancellationToken.None);

        Assert.Equal(["A", "B", "C"], order);
        Assert.All(results, r => Assert.Equal(PipelineStepStatus.Completed, r.Status));
    }

    [Fact]
    public async Task Execute_IndependentNodes_ParallelReducesWallTime()
    {
        var dag = PipelineDag.CreateBuilder()
            .Add("Slow1", new StubOperator("Slow1", TimeSpan.FromMilliseconds(500)))
            .Add("Slow2", new StubOperator("Slow2", TimeSpan.FromMilliseconds(500)))
            .Add("Slow3", new StubOperator("Slow3", TimeSpan.FromMilliseconds(500)))
            .Build();

        var stopwatch = Stopwatch.StartNew();
        var results = await CreateExecutor().ExecuteAsync(dag, CreateContext(), CancellationToken.None);
        stopwatch.Stop();

        // 三个 500ms 无依赖节点并行：总耗时应显著小于串行 1500ms
        Assert.True(stopwatch.ElapsedMilliseconds < 1200,
            $"Parallel DAG took {stopwatch.ElapsedMilliseconds}ms; expected < 1200ms for 3x500ms independent nodes.");
        Assert.Equal(3, results.Count);
    }

    [Fact]
    public async Task Execute_DiamondTopology_SerializesSharedDependency()
    {
        var order = new List<string>();
        var dag = PipelineDag.CreateBuilder()
            .Add("Root", new OrderOperator("Root", order))
            .Add("Left", new OrderOperator("Left", order), dependsOn: ["Root"])
            .Add("Right", new OrderOperator("Right", order), dependsOn: ["Root"])
            .Add("Join", new OrderOperator("Join", order), dependsOn: ["Left", "Right"])
            .Build();

        await CreateExecutor().ExecuteAsync(dag, CreateContext(), CancellationToken.None);

        Assert.Equal(0, order.IndexOf("Root"));
        Assert.Equal(4, order.Count);
        Assert.True(order.IndexOf("Join") > order.IndexOf("Left"));
        Assert.True(order.IndexOf("Join") > order.IndexOf("Right"));
    }

    // ---------- 条件节点 ----------

    [Fact]
    public async Task Execute_ConditionFalse_SkipsNodeAndContinues()
    {
        var dag = PipelineDag.CreateBuilder()
            .Add("Always", new StubOperator("Always"), when: _ => false)
            .Add("Next", new StubOperator("Next", stateField: "s", stateValue: "ran"), dependsOn: ["Always"])
            .Build();

        var context = CreateContext();
        var results = await CreateExecutor().ExecuteAsync(dag, context, CancellationToken.None);

        var skipped = Assert.Single(results, r => r.Status == PipelineStepStatus.Skipped);
        Assert.Equal("Always", skipped.Name);
        Assert.Contains("Next", results.Select(r => r.Name));
        Assert.Equal("ran", context.State.Extensions["Next:s"]);
    }

    // ---------- 重试 / 降级 ----------

    [Fact]
    public async Task Execute_RetrySucceeds_ReportsRetried()
    {
        var dag = PipelineDag.CreateBuilder()
            .Add("Flaky", new StubOperator("Flaky", failTimes: 2), maxRetries: 3)
            .Build();

        var results = await CreateExecutor().ExecuteAsync(dag, CreateContext(), CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal(PipelineStepStatus.Retried, result.Status);
        Assert.Equal(3, result.Attempts);
    }

    [Fact]
    public async Task Execute_RetriesExhaustedWithDegrade_SkipsAndContinues()
    {
        var dag = PipelineDag.CreateBuilder()
            .Add("Failing", new StubOperator("Failing", failTimes: 99), maxRetries: 1, degradeOnFailure: true)
            .Add("After", new StubOperator("After", stateField: "s", stateValue: "done"), dependsOn: ["Failing"])
            .Build();

        var context = CreateContext();
        var results = await CreateExecutor().ExecuteAsync(dag, context, CancellationToken.None);

        var degraded = Assert.Single(results, r => r.Status == PipelineStepStatus.Degraded);
        Assert.Equal("Failing", degraded.Name);
        Assert.Equal(PipelineStepStatus.Completed, Assert.Single(results, r => r.Name == "After").Status);
        Assert.Equal("done", context.State.Extensions["After:s"]);
    }

    [Fact]
    public async Task Execute_RetriesExhaustedWithoutDegrade_Throws()
    {
        var dag = PipelineDag.CreateBuilder()
            .Add("Failing", new StubOperator("Failing", failTimes: 99), maxRetries: 1)
            .Build();

        var ex = await Assert.ThrowsAsync<PipelineExecutionException>(
            () => CreateExecutor().ExecuteAsync(dag, CreateContext(), CancellationToken.None));
        Assert.Equal("Failing", ex.NodeName);
    }

    // ---------- 超时 ----------

    [Fact]
    public async Task Execute_NodeTimeout_TriggersRetry()
    {
        var dag = PipelineDag.CreateBuilder()
            .Add("Slow", new StubOperator("Slow", TimeSpan.FromSeconds(5)),
                maxRetries: 1, timeout: TimeSpan.FromMilliseconds(100), degradeOnFailure: true)
            .Build();

        var results = await CreateExecutor().ExecuteAsync(dag, CreateContext(), CancellationToken.None);
        var result = Assert.Single(results);
        Assert.Equal(PipelineStepStatus.Degraded, result.Status);
        Assert.Equal(2, result.Attempts);
    }

    // ---------- 环检测 ----------

    [Fact]
    public async Task Execute_CyclicDag_ThrowsInvalidOperation()
    {
        var dag = PipelineDag.CreateBuilder()
            .Add("A", new StubOperator("A"), dependsOn: ["B"])
            .Add("B", new StubOperator("B"), dependsOn: ["A"])
            .Build();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateExecutor().ExecuteAsync(dag, CreateContext(), CancellationToken.None));
    }

    // ---------- IR/状态一致性：线性 DAG vs 并行 DAG ----------

    [Fact]
    public async Task Execute_ParallelTopology_ProducesSameStateAsSequential()
    {
        var sequential = PipelineDag.CreateBuilder()
            .Add("A", new StubOperator("A", stateField: "s", stateValue: "a"))
            .Add("B", new StubOperator("B", stateField: "s", stateValue: "b"), dependsOn: ["A"])
            .Build();
        var parallel = PipelineDag.CreateBuilder()
            .Add("A", new StubOperator("A", stateField: "s", stateValue: "a"))
            .Add("B", new StubOperator("B", stateField: "s", stateValue: "b"))
            .Build();

        var seqContext = CreateContext();
        var parContext = CreateContext();
        await CreateExecutor().ExecuteAsync(sequential, seqContext, CancellationToken.None);
        await CreateExecutor().ExecuteAsync(parallel, parContext, CancellationToken.None);

        Assert.Equal(seqContext.State.Extensions, parContext.State.Extensions);
        Assert.All(seqContext.State.StepTimings.Keys, name => Assert.Contains(name, parContext.State.StepTimings));
    }

    /// <summary>记录执行顺序的算子。</summary>
    private sealed class OrderOperator(string name, List<string> order) : IPipelineOperator
    {
        public string Name { get; } = name;
        public Task ExecuteAsync(SubtitleWorkflowContext context, CancellationToken cancellationToken = default)
        {
            lock (order)
            {
                order.Add(Name);
            }
            return Task.CompletedTask;
        }
    }
}
