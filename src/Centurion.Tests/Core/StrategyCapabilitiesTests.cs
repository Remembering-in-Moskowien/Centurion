using Centurion.Abstractions.Providers;
using Centurion.Abstractions.Strategy;
using Centurion.Core.Providers;
using Centurion.Core.Providers.Asr;
using Centurion.Core.Workflow.DependencyInjection;
using Centurion.Core.Workflow.Pipeline.Operators;
using Centurion.Core.Workflow.Strategy.SentenceSplit;
using Centurion.Models.Asr;
using Centurion.Models.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Centurion.Tests.Core;

/// <summary>
/// Strategy/operator fusion tests: strategies declare capabilities (e.g. forced-aligned timestamps),
/// the transcription operator exposes the aggregated capabilities of its chain, and the DAG assembler
/// uses them to prune redundant stages (e.g. Force Alignment for CrispASR-Qwen3).
/// </summary>
public sealed class StrategyCapabilitiesTests
{
    private static ProviderFactory CreateFactory()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCenturionCore();
        return (ProviderFactory)services.BuildServiceProvider().GetRequiredService<IProviderFactory>();
    }

    private static AsrOptions NoKey() => new(AsrProvider.OpenAI, null, null);

    [Fact]
    public void LocalAsrProvider_ExposesStrategy_WithQwenDeclaringAlignedTimestamps()
    {
        var factory = CreateFactory();
        var chain = factory.CreateAsrChain("crispasr", null, NoKey(), ProviderProfile.Fast);

        var provider = Assert.IsType<LocalAsrProvider>(chain[0]);
        Assert.Equal("CrispASR (Qwen3)", provider.Strategy.StrategyName);
        Assert.Equal(StrategyCapabilities.AlignedTimestamps, provider.Strategy.Capabilities);
    }

    [Fact]
    public void TranscribeOperator_AggregatesAlignedTimestamps_FromQwenChain()
    {
        var factory = CreateFactory();
        var chain = factory.CreateAsrChain("crispasr", null, NoKey(), ProviderProfile.Fast);
        var op = new TranscribeOperator(chain, factory, NullLogger<TranscribeOperator>.Instance);

        Assert.True(op.Capabilities.HasFlag(StrategyCapabilities.AlignedTimestamps));
        Assert.Single(op.Strategies);
        Assert.Equal("CrispASR (Qwen3)", op.Strategies[0].StrategyName);
    }

    [Fact]
    public void WhisperCppStrategy_DeclaresNoAlignedTimestamps()
    {
        var factory = CreateFactory();
        var chain = factory.CreateAsrChain("whispercpp", null, NoKey(), ProviderProfile.Fast);

        var provider = Assert.IsType<LocalAsrProvider>(chain[0]);
        Assert.Equal(StrategyCapabilities.None, provider.Strategy.Capabilities);
    }

    [Fact]
    public void RuleSplitStrategy_DeclaresRuleName_AndNoCapabilities()
    {
        var strategy = new AggressiveRuleSplitStrategy();

        Assert.Equal("rule", strategy.StrategyName);
        Assert.Equal(StrategyCapabilities.None, strategy.Capabilities);
    }
}
