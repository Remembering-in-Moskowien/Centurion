using Centurion.Core.Ocr;
using Centurion.Core.Pipeline.Operators;
using Xunit;

namespace Centurion.Tests.Core;

/// <summary>OCR 多后端解析与端点/模型默认值测试。</summary>
public sealed class OcrClientTests
{
    [Theory]
    [InlineData(OcrBackend.Zhipu, "glm-ocr", "https://open.bigmodel.cn/api/paas/v4/chat/completions")]
    [InlineData(OcrBackend.Ollama, "qwen2.5vl:7b", "http://localhost:11434/v1/chat/completions")]
    [InlineData(OcrBackend.LlamaCpp, "local-model", "http://127.0.0.1:8080/v1/chat/completions")]
    public void ResolveBackend_Defaults(OcrBackend backend, string expectedModel, string expectedEndpoint)
    {
        var (endpoint, model) = OcrClient.ResolveBackend(backend, null, null);
        Assert.Equal(expectedEndpoint, endpoint);
        Assert.Equal(expectedModel, model);
    }

    [Fact]
    public void ResolveBackend_ExplicitOverridesDefault()
    {
        var (endpoint, model) = OcrClient.ResolveBackend(OcrBackend.Ollama, "llava:13b", "http://10.0.0.5:11434/v1/chat/completions");
        Assert.Equal("http://10.0.0.5:11434/v1/chat/completions", endpoint);
        Assert.Equal("llava:13b", model);
    }

    [Theory]
    [InlineData("ollama", OcrBackend.Ollama)]
    [InlineData("OLLAMA", OcrBackend.Ollama)]
    [InlineData("llamacpp", OcrBackend.LlamaCpp)]
    [InlineData("llama-cpp", OcrBackend.LlamaCpp)]
    [InlineData("zhipu", OcrBackend.Zhipu)]
    [InlineData(null, OcrBackend.Zhipu)]
    [InlineData("bogus", OcrBackend.Zhipu)]
    public void ParseBackend_MapsKnownAndFallsBack(string? value, OcrBackend expected)
    {
        Assert.Equal(expected, OcrExtractOperator.ParseBackend(value));
    }
}
