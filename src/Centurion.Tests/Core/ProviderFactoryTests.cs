using Centurion.Abstractions.Providers;
using Centurion.Abstractions.Strategy;
using Centurion.Core.Providers;
using Centurion.Core.Workflow.DependencyInjection;
using Centurion.Models.Asr;
using Centurion.Models.Providers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Centurion.Tests.Core;

/// <summary>Focused tests for Provider factory resolution and profiles (stage ⑨ acceptance).</summary>
public sealed class ProviderFactoryTests
{
    private static ProviderFactory CreateFactory()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCenturionCore();
        // Note: ProviderRegistry is lazily built and holds the IServiceProvider; the container must not be disposed during tests.
        return (ProviderFactory)services.BuildServiceProvider().GetRequiredService<IProviderFactory>();
    }

    private static AsrOptions NoKey() => new(AsrProvider.OpenAI, null, null);

    [Theory]
    [InlineData("openai", "openai")]
    [InlineData("groq", "groq")]
    [InlineData("dashscope", "dashscope")]
    [InlineData("deepgram", "deepgram")]
    [InlineData("whispercpp", "whispercpp")]
    [InlineData("crispasr", "crispasr-qwen")]
    [InlineData("crispasr-whisper", "crispasr-whisper")]
    public void CreateAsrChain_ResolvesPrimary(string engine, string expectedPrimary)
    {
        var factory = CreateFactory();
        var chain = factory.CreateAsrChain(engine, null, NoKey(), ProviderProfile.Fast);
        Assert.Equal(expectedPrimary, chain[0].Name);
    }

    [Fact]
    public void CreateAsrChain_CloudEngine_AddsLocalBackup()
    {
        var factory = CreateFactory();
        var chain = factory.CreateAsrChain("openai", null,
            new AsrOptions(AsrProvider.OpenAI, "test-key", null), ProviderProfile.Fast);

        Assert.Equal(2, chain.Count);
        Assert.Equal("openai", chain[0].Name);
        Assert.Equal(ProviderKind.Cloud, chain[0].Capabilities.Kind);
        Assert.Equal(ProviderKind.Local, chain[1].Capabilities.Kind); // local fallback
    }

    [Fact]
    public void CreateAsrChain_CloudWithoutKey_StillResolvesLocalBackup()
    {
        // No API key: the primary cloud provider has IsAvailable=false, so chain execution automatically falls back to local (without crashing).
        var factory = CreateFactory();
        var chain = factory.CreateAsrChain("openai", null, NoKey(), ProviderProfile.Fast);

        Assert.Equal("openai", chain[0].Name);
        Assert.Equal("crispasr-qwen", chain[1].Name);
    }

    [Fact]
    public void CreateAsrChain_OfflineProfile_ForcesLocalEvenWhenCloudConfigured()
    {
        var factory = CreateFactory();
        var chain = factory.CreateAsrChain("openai", null,
            new AsrOptions(AsrProvider.OpenAI, "test-key", null), ProviderProfile.Offline);

        Assert.Single(chain);
        Assert.Equal("crispasr-qwen", chain[0].Name);
        Assert.Equal(ProviderKind.Local, chain[0].Capabilities.Kind);
    }

    [Fact]
    public void CreateAsrChain_CheapProfile_PrefersLocal()
    {
        var factory = CreateFactory();
        var chain = factory.CreateAsrChain("openai", null,
            new AsrOptions(AsrProvider.OpenAI, "test-key", null), ProviderProfile.Cheap);

        Assert.Equal("crispasr-qwen", chain[0].Name);
        Assert.Equal("openai", chain[1].Name);
    }

    [Fact]
    public void CreateAsrChain_UnknownEngine_Throws()
    {
        var factory = CreateFactory();
        Assert.Throws<NotSupportedException>(() =>
            factory.CreateAsrChain("invalid-engine", null, NoKey(), ProviderProfile.Fast));
    }

    [Fact]
    public void CreateLlmProvider_Unknown_Throws()
    {
        var factory = CreateFactory();
        Assert.Throws<NotSupportedException>(() => factory.CreateLlmProvider("nope", null, null, null));
    }

    [Fact]
    public void CreateLlmProvider_Known_Resolves()
    {
        var factory = CreateFactory();
        var provider = factory.CreateLlmProvider("ollama", null, null, null);
        Assert.Equal("ollama-llm", provider.Name);
        Assert.Equal(ProviderKind.Local, provider.Capabilities.Kind);
    }

    [Fact]
    public void CreateOcrProvider_Known_Resolves()
    {
        var factory = CreateFactory();
        var provider = factory.CreateOcrProvider("ollama", null, null, null);
        Assert.Equal("ollama", provider.Name);
    }

    [Fact]
    public void CreateOcrProvider_Unknown_Throws()
    {
        var factory = CreateFactory();
        Assert.Throws<NotSupportedException>(() => factory.CreateOcrProvider("nope", null, null, null));
    }

    [Fact]
    public void CreateOcrProvider_RapidOcr_Resolves()
    {
        var factory = CreateFactory();
        var provider = factory.CreateOcrProvider("rapidocr", null, null, null);
        Assert.Equal("rapidocr", provider.Name);
        Assert.Equal(ProviderKind.Local, provider.Capabilities.Kind);
    }
}
