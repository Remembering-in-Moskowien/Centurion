using Centurion.Core.Capabilities.Infrastructure.Llm;using Centurion.Models.Llm;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Centurion.Tests.Core;

/// <summary>
/// LLM 端点解析测试：提供商名/URL 推断、默认端点与默认模型补全、Auto 回退规则。
/// </summary>
public sealed class LlmEndpointParserTests
{
    private static readonly NullLogger<LlmEndpointParserTests> Logger = new();

    [Theory]
    [InlineData("deepseek", LlmProvider.DeepSeek)]
    [InlineData("DeepSeek", LlmProvider.DeepSeek)]
    [InlineData("ds", LlmProvider.DeepSeek)]
    [InlineData("kimi", LlmProvider.Moonshot)]
    [InlineData("moonshot", LlmProvider.Moonshot)]
    [InlineData("glm", LlmProvider.Zhipu)]
    [InlineData("openrouter", LlmProvider.OpenRouter)]
    [InlineData("groq", LlmProvider.Groq)]
    [InlineData("silicon", LlmProvider.SiliconFlow)]
    [InlineData("aliyun", LlmProvider.DashScope)]
    [InlineData("azure", LlmProvider.Azure)]
    [InlineData("ollama", LlmProvider.Ollama)]
    [InlineData("openai", LlmProvider.OpenAI)]
    [InlineData("", LlmProvider.Auto)]
    [InlineData("unknown-provider", LlmProvider.Auto)]
    public void ParseProvider_RecognizesNamesAndAliases(string name, LlmProvider expected)
    {
        Assert.Equal(expected, LlmEndpointParser.ParseProvider(name));
    }

    [Theory]
    [InlineData("https://api.deepseek.com", LlmProvider.DeepSeek)]
    [InlineData("https://api.moonshot.cn/v1", LlmProvider.Moonshot)]
    [InlineData("https://open.bigmodel.cn/api/paas/v4", LlmProvider.Zhipu)]
    [InlineData("https://openrouter.ai/api/v1", LlmProvider.OpenRouter)]
    [InlineData("https://api.groq.com/openai/v1", LlmProvider.Groq)]
    [InlineData("https://api.siliconflow.cn/v1", LlmProvider.SiliconFlow)]
    [InlineData("https://dashscope.aliyuncs.com/compatible-mode/v1", LlmProvider.DashScope)]
    [InlineData("https://ark.cn-beijing.volces.com/api/v3", LlmProvider.Ark)]
    [InlineData("https://my-resource.openai.azure.com/openai/v1", LlmProvider.Azure)]
    [InlineData("https://api.openai.com/v1", LlmProvider.OpenAI)]
    [InlineData("http://localhost:11434", LlmProvider.Ollama)]
    [InlineData("http://127.0.0.1:11434", LlmProvider.Ollama)]
    [InlineData("https://custom.example.com/v1", LlmProvider.Auto)]
    [InlineData("not-a-url", LlmProvider.Auto)]
    public void InferFromUrl_RecognizesHosts(string url, LlmProvider expected)
    {
        Assert.Equal(expected, LlmEndpointParser.InferFromUrl(url));
    }

    [Fact]
    public void Resolve_ExplicitProvider_UsesDefaultEndpointAndModel()
    {
        var (provider, baseUrl, model) = LlmEndpointParser.Resolve(
            new LlmOptions { Provider = LlmProvider.DeepSeek, ApiKey = "key" }, Logger);

        Assert.Equal(LlmProvider.DeepSeek, provider);
        Assert.Equal("https://api.deepseek.com", baseUrl);
        Assert.Equal("deepseek-chat", model);
    }

    [Fact]
    public void Resolve_ExplicitProvider_WithCustomBaseUrlAndModel_KeepsThem()
    {
        var (provider, baseUrl, model) = LlmEndpointParser.Resolve(
            new LlmOptions { Provider = LlmProvider.Moonshot, ApiKey = "key", BaseUrl = "https://proxy.example.com/v1", Model = "moonshot-v1-32k" }, Logger);

        Assert.Equal(LlmProvider.Moonshot, provider);
        Assert.Equal("https://proxy.example.com/v1", baseUrl);
        Assert.Equal("moonshot-v1-32k", model);
    }

    [Fact]
    public void Resolve_AutoWithUrl_InfersProviderAndFillsDefaults()
    {
        var (provider, baseUrl, model) = LlmEndpointParser.Resolve(
            new LlmOptions { BaseUrl = "https://api.groq.com/openai/v1", ApiKey = "key" }, Logger);

        Assert.Equal(LlmProvider.Groq, provider);
        Assert.Equal("https://api.groq.com/openai/v1", baseUrl);
        Assert.Equal("llama-3.3-70b-versatile", model);
    }

    [Fact]
    public void Resolve_AutoNoUrl_WithKey_FallsBackToOpenAI()
    {
        var (provider, baseUrl, model) = LlmEndpointParser.Resolve(
            new LlmOptions { ApiKey = "sk-test" }, Logger);

        Assert.Equal(LlmProvider.OpenAI, provider);
        Assert.Equal("https://api.openai.com/v1", baseUrl);
        Assert.Equal("gpt-4o-mini", model);
    }

    [Fact]
    public void Resolve_AutoNoUrl_NoKey_FallsBackToOllama()
    {
        var (provider, baseUrl, model) = LlmEndpointParser.Resolve(new LlmOptions(), Logger);

        Assert.Equal(LlmProvider.Ollama, provider);
        Assert.Equal("http://localhost:11434", baseUrl);
        Assert.Equal("llama3.2", model);
    }

    [Fact]
    public void Resolve_ProviderName_StringIsParsed()
    {
        var (provider, baseUrl, model) = LlmEndpointParser.Resolve(
            new LlmOptions { ProviderName = "zhipu", ApiKey = "key" }, Logger);

        Assert.Equal(LlmProvider.Zhipu, provider);
        Assert.Equal("https://open.bigmodel.cn/api/paas/v4", baseUrl);
        Assert.Equal("glm-4-flash", model);
    }

    [Fact]
    public void Resolve_OpenRouter_HasNoDefaultModel_KeepsNullWithWarning()
    {
        var (provider, baseUrl, model) = LlmEndpointParser.Resolve(
            new LlmOptions { Provider = LlmProvider.OpenRouter, ApiKey = "key" }, Logger);

        Assert.Equal(LlmProvider.OpenRouter, provider);
        Assert.Equal("https://openrouter.ai/api/v1", baseUrl);
        Assert.Null(model);
    }

    [Fact]
    public void Resolve_ProviderNameWinsOverUrlInference()
    {
        // 显式 ProviderName 优先于 URL 推断：URL 指向 OpenAI，但 ProviderName 指定 DeepSeek
        var (provider, baseUrl, _) = LlmEndpointParser.Resolve(
            new LlmOptions { ProviderName = "deepseek", BaseUrl = "https://api.openai.com/v1", ApiKey = "key" }, Logger);

        Assert.Equal(LlmProvider.DeepSeek, provider);
        Assert.Equal("https://api.openai.com/v1", baseUrl);
    }

    [Fact]
    public void Registry_GetDisplayName_CoversAllProviders()
    {
        foreach (LlmProvider provider in Enum.GetValues<LlmProvider>())
        {
            var name = LlmProviderRegistry.GetDisplayName(provider);
            Assert.False(string.IsNullOrWhiteSpace(name));
        }
    }
}
