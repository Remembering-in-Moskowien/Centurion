using Centurion.Abstractions.Providers;
using Centurion.Core.Providers;
using Centurion.Models.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Centurion.Tests.Core;

/// <summary>Focused tests for the Provider fallback chain and strategy (stage ⑨ acceptance).</summary>
public sealed class ProviderChainTests
{
    private sealed class FakeProvider(
        string name,
        ProviderKind kind,
        bool available,
        bool throws,
        double cost) : IProvider
    {
        public string Name { get; } = name;
        public string DisplayName { get; } = name;
        public ProviderCapabilities Capabilities { get; } = kind == ProviderKind.Cloud
            ? ProviderCapabilities.Cloud(0, cost, ProviderLatency.Medium, ProviderQualityLevel.Normal, "test")
            : ProviderCapabilities.Local(false, ProviderLatency.Low, ProviderQualityLevel.Normal, "test");
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(available);
        public bool Throws { get; } = throws;
    }

    private static ProviderResult<string> Ok(string provider, double cost = 0) =>
        new("ok", new ProviderUsage { ProviderName = provider, EstimatedCostUsd = cost, TokensIn = 10, TokensOut = 5 });

    private static ProviderPolicies NewPolicies() =>
        new(ProviderPolicyOptions.ForProfile(ProviderProfile.Fast));

    [Fact]
    public async Task UnavailablePrimary_FallsBackToSecondary()
    {
        var chain = new IProvider[]
        {
            new FakeProvider("cloud", ProviderKind.Cloud, available: false, throws: false, cost: 0),
            new FakeProvider("local", ProviderKind.Local, available: true, throws: false, cost: 0)
        };

        var result = await ProviderChain.ExecuteAsync(
            chain,
            (p, ct) => Task.FromResult(Ok(p.Name)),
            NewPolicies(),
            budgetUsdPerRun: 0,
            NullLogger.Instance,
            CancellationToken.None);

        Assert.Equal("ok", result.Value);
        Assert.Equal("local", result.Usage.ProviderName);
    }

    [Fact]
    public async Task ThrowingPrimary_RetriesThenFallsBackToSecondary()
    {
        var calls = 0;
        var chain = new IProvider[]
        {
            new FakeProvider("broken", ProviderKind.Local, available: true, throws: true, cost: 0),
            new FakeProvider("good", ProviderKind.Local, available: true, throws: false, cost: 0)
        };

        var result = await ProviderChain.ExecuteAsync(
            chain,
            (p, ct) =>
            {
                calls++;
                if (p.Name == "broken")
                    throw new ProviderExecutionException("boom", p.Name);
                return Task.FromResult(Ok(p.Name));
            },
            NewPolicies(),
            budgetUsdPerRun: 0,
            NullLogger.Instance,
            CancellationToken.None);

        Assert.Equal("good", result.Usage.ProviderName);
        Assert.True(calls >= 2, $"expected retry+switch, got {calls} calls");
    }

    [Fact]
    public async Task Budget_AccumulatesAcrossChains_AndSkipsCloudAfterExceeded()
    {
        var policies = NewPolicies();
        var expensive = new IProvider[]
        {
            new FakeProvider("cloud-expensive", ProviderKind.Cloud, available: true, throws: false, cost: 2.0)
        };
        var later = new IProvider[]
        {
            new FakeProvider("cloud-cheap", ProviderKind.Cloud, available: true, throws: false, cost: 0.5),
            new FakeProvider("local", ProviderKind.Local, available: true, throws: false, cost: 0)
        };
        var invoked = new List<string>();

        // First chain: the primary cloud provider succeeds, costing 2.0 (budget 1.0 → over the limit, but the successful result is still returned).
        await ProviderChain.ExecuteAsync(
            expensive,
            (p, ct) =>
            {
                invoked.Add(p.Name);
                return Task.FromResult(Ok(p.Name, 2.0));
            },
            policies,
            budgetUsdPerRun: 1.0,
            NullLogger.Instance,
            CancellationToken.None);

        // Second chain (the same policies instance shares the budget): the budget is exhausted → skip subsequent cloud providers and fall back to local.
        var result = await ProviderChain.ExecuteAsync(
            later,
            (p, ct) =>
            {
                invoked.Add(p.Name);
                return Task.FromResult(Ok(p.Name));
            },
            policies,
            budgetUsdPerRun: 1.0,
            NullLogger.Instance,
            CancellationToken.None);

        Assert.Contains("cloud-expensive", invoked);
        Assert.DoesNotContain("cloud-cheap", invoked); // subsequent cloud providers are skipped after the budget is exhausted
        Assert.Contains("local", invoked);
        Assert.Equal("local", result.Usage.ProviderName);
        Assert.True(policies.BudgetSpentUsd >= 2.0, "累计花费应 >= 2.0");
    }

    [Fact]
    public async Task Usage_AggregatedAcrossProviders()
    {
        var chain = new IProvider[]
        {
            new FakeProvider("first", ProviderKind.Local, available: true, throws: true, cost: 0),
            new FakeProvider("second", ProviderKind.Local, available: true, throws: false, cost: 0)
        };

        var result = await ProviderChain.ExecuteAsync(
            chain,
            (p, ct) => p.Name == "first"
                ? throw new ProviderUnavailableException("no", p.Name)
                : Task.FromResult(new ProviderResult<string>("ok", new ProviderUsage
                {
                    ProviderName = "second", TokensIn = 100, TokensOut = 50,
                    CacheHits = 3, EstimatedCostUsd = 0.12
                })),
            NewPolicies(),
            budgetUsdPerRun: 0,
            NullLogger.Instance,
            CancellationToken.None);

        Assert.Equal(100, result.Usage.TokensIn);
        Assert.Equal(50, result.Usage.TokensOut);
        Assert.Equal(3, result.Usage.CacheHits);
        Assert.Equal(0.12, result.Usage.EstimatedCostUsd, precision: 6);
    }

    [Fact]
    public async Task AllUnavailable_ThrowsProviderUnavailable()
    {
        var chain = new IProvider[]
        {
            new FakeProvider("a", ProviderKind.Cloud, available: false, throws: false, cost: 0),
            new FakeProvider("b", ProviderKind.Local, available: false, throws: false, cost: 0)
        };

        await Assert.ThrowsAsync<ProviderUnavailableException>(() => ProviderChain.ExecuteAsync(
            chain,
            (p, ct) => Task.FromResult(Ok(p.Name)),
            NewPolicies(),
            budgetUsdPerRun: 0,
            NullLogger.Instance,
            CancellationToken.None));
    }

    [Fact]
    public async Task AllThrew_ThrowsProviderExecution()
    {
        var chain = new IProvider[]
        {
            new FakeProvider("a", ProviderKind.Local, available: true, throws: true, cost: 0),
            new FakeProvider("b", ProviderKind.Local, available: true, throws: true, cost: 0)
        };

        await Assert.ThrowsAsync<ProviderExecutionException>(() => ProviderChain.ExecuteAsync<string>(
            chain,
            (p, ct) => throw new ProviderExecutionException("boom", p.Name),
            NewPolicies(),
            budgetUsdPerRun: 0,
            NullLogger.Instance,
            CancellationToken.None));
    }

    [Fact]
    public async Task EmptyChain_ThrowsProviderExecution()
    {
        await Assert.ThrowsAsync<ProviderExecutionException>(() => ProviderChain.ExecuteAsync(
            Array.Empty<IProvider>(),
            (p, ct) => Task.FromResult(Ok(p.Name)),
            NewPolicies(),
            budgetUsdPerRun: 0,
            NullLogger.Instance,
            CancellationToken.None));
    }
}
