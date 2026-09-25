using Centurion.Core.Asr;
using Centurion.Models.Asr;
using Xunit;

namespace Centurion.Tests.Core;

/// <summary>云端 ASR 端点解析测试。</summary>
public sealed class AsrEndpointParserTests
{
    [Theory]
    [InlineData("openai", AsrProvider.OpenAI, "whisper-1")]
    [InlineData("whisper-api", AsrProvider.OpenAI, "whisper-1")]
    [InlineData("groq", AsrProvider.Groq, "whisper-large-v3")]
    [InlineData("dashscope", AsrProvider.DashScope, "paraformer-realtime-v2")]
    [InlineData("aliyun", AsrProvider.DashScope, "paraformer-realtime-v2")]
    [InlineData("qwen", AsrProvider.DashScope, "paraformer-realtime-v2")]
    [InlineData("deepgram", AsrProvider.Deepgram, "nova-2")]
    public void Resolve_CloudEngine_MapsProviderAndModel(string engine, AsrProvider provider, string defaultModel)
    {
        var resolved = AsrEndpointParser.Resolve(engine);
        Assert.NotNull(resolved);
        Assert.Equal(provider, resolved.Value.Provider);
        Assert.Equal(defaultModel, resolved.Value.DefaultModel);
        Assert.False(string.IsNullOrWhiteSpace(resolved.Value.Endpoint));
    }

    [Theory]
    [InlineData("crispasr")]
    [InlineData("whisper")]
    [InlineData("whispercpp")]
    [InlineData("bogus")]
    public void Resolve_LocalOrUnknown_ReturnsNull(string engine)
    {
        Assert.Null(AsrEndpointParser.Resolve(engine));
    }

    [Theory]
    [InlineData("openai", true)]
    [InlineData("groq", true)]
    [InlineData("dashscope", true)]
    [InlineData("deepgram", true)]
    [InlineData("crispasr", false)]
    [InlineData("whisper", false)]
    public void IsCloud_OnlyCloudEngines(string engine, bool expected)
    {
        Assert.Equal(expected, AsrEndpointParser.IsCloud(engine));
    }
}
