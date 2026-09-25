using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Ocr;

/// <summary>OCR 推理后端。</summary>
public enum OcrBackend
{
    /// <summary>智谱开放平台 GLM-OCR（云端，需 API 密钥）。</summary>
    Zhipu,

    /// <summary>本地 Ollama 视觉模型（qwen2.5vl / llava 等，免密钥）。</summary>
    Ollama,

    /// <summary>本地 llama-server（GGUF 视觉模型，免密钥，服务需已启动）。</summary>
    LlamaCpp
}

/// <summary>
/// 多后端 OCR 客户端：通过 OpenAI 兼容的 chat/completions 端点，
/// 以多模态消息（文本指令 + 图片 base64）对单张图片做字幕级 OCR。
/// 支持云端（智谱 GLM-OCR）与本地推理（Ollama / llama-server）两种路径。
/// </summary>
public sealed class OcrClient(HttpClient httpClient, ILogger<OcrClient> logger)
{
    /// <summary>智谱开放平台默认 chat/completions 端点。</summary>
    public const string ZhipuBaseUrl = "https://open.bigmodel.cn/api/paas/v4/chat/completions";

    /// <summary>智谱默认 OCR 模型（账号未开通时可换 glm-4v-plus 等视觉模型）。</summary>
    public const string ZhipuDefaultModel = "glm-ocr";

    /// <summary>本地 Ollama 默认端点（OpenAI 兼容）。</summary>
    public const string OllamaBaseUrl = "http://localhost:11434/v1/chat/completions";

    /// <summary>Ollama 默认视觉模型。</summary>
    public const string OllamaDefaultModel = "qwen2.5vl:7b";

    /// <summary>本地 llama-server 默认端点（OpenAI 兼容）。</summary>
    public const string LlamaCppBaseUrl = "http://127.0.0.1:8080/v1/chat/completions";

    /// <summary>llama-server 默认模型占位（OpenAI 兼容实现忽略 model 内容）。</summary>
    public const string LlamaCppDefaultModel = "local-model";

    /// <summary>OCR 指令：只输出字幕文本，每行一条，不做任何解释。</summary>
    private const string SystemPrompt =
        "You are a subtitle OCR engine. Extract ALL visible subtitle/caption text from the image. " +
        "Output ONLY the subtitle lines, one subtitle per line, preserving the original language exactly. " +
        "Do not describe the image, do not add numbering, do not translate, do not add explanations. " +
        "If there is no subtitle text, output exactly: [NO_TEXT]";

    /// <summary>
    /// 按后端解析最终端点与模型：显式配置优先，否则使用后端默认值。
    /// </summary>
    /// <param name="backend">OCR 后端。</param>
    /// <param name="model">显式模型；为空时用后端默认。</param>
    /// <param name="baseUrl">显式端点；为空时用后端默认。</param>
    /// <returns>（端点, 模型）。</returns>
    public static (string Endpoint, string Model) ResolveBackend(
        OcrBackend backend, string? model, string? baseUrl) => backend switch
    {
        OcrBackend.Zhipu => (
            string.IsNullOrWhiteSpace(baseUrl) ? ZhipuBaseUrl : baseUrl,
            string.IsNullOrWhiteSpace(model) ? ZhipuDefaultModel : model),
        OcrBackend.Ollama => (
            string.IsNullOrWhiteSpace(baseUrl) ? OllamaBaseUrl : baseUrl,
            string.IsNullOrWhiteSpace(model) ? OllamaDefaultModel : model),
        OcrBackend.LlamaCpp => (
            string.IsNullOrWhiteSpace(baseUrl) ? LlamaCppBaseUrl : baseUrl,
            string.IsNullOrWhiteSpace(model) ? LlamaCppDefaultModel : model),
        _ => throw new ArgumentOutOfRangeException(nameof(backend), backend, "Unknown OCR backend")
    };

    /// <summary>
    /// 探测本地 OCR 服务是否在线（GET /models，兼容 Ollama 与 llama-server 的 OpenAI 兼容接口）。
    /// 云端后端返回 true（不预先探测，失败由请求自然报错）。
    /// </summary>
    /// <param name="backend">OCR 后端。</param>
    /// <param name="baseUrl">显式端点；为空时按后端默认。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>服务是否可访问。</returns>
    public async Task<bool> ProbeAsync(OcrBackend backend, string? baseUrl, CancellationToken cancellationToken)
    {
        if (backend == OcrBackend.Zhipu)
            return true;

        var (endpoint, _) = ResolveBackend(backend, null, baseUrl);
        var modelsUrl = endpoint.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
            ? endpoint[..^"/chat/completions".Length] + "/models"
            : endpoint.TrimEnd('/') + "/models";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, modelsUrl);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "OCR backend probe failed at {ModelsUrl}", modelsUrl);
            return false;
        }
    }

    /// <summary>
    /// 对单张图片执行 OCR，返回提取的字幕文本（可能多行；无字幕时为 "[NO_TEXT]"）。
    /// </summary>
    /// <param name="imagePath">图片文件路径（jpg/png 等）。</param>
    /// <param name="backend">OCR 后端（云端/本地）。</param>
    /// <param name="model">OCR 模型名；为空时按后端默认。</param>
    /// <param name="apiKey">API 密钥；仅智谱云端必填，本地后端可空。</param>
    /// <param name="baseUrl">端点地址；为空时按后端默认。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>提取的字幕文本。</returns>
    /// <exception cref="InvalidOperationException">云端密钥缺失、请求失败或响应无内容时抛出。</exception>
    public async Task<string> OcrImageAsync(
        string imagePath,
        OcrBackend backend,
        string? model,
        string? apiKey,
        string? baseUrl,
        CancellationToken cancellationToken)
    {
        if (backend == OcrBackend.Zhipu && string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException(
                "Zhipu GLM-OCR requires an API key. Provide --ocr-api-key (or switch to --ocr-backend ollama/llamacpp for local inference).");

        if (!File.Exists(imagePath))
            throw new FileNotFoundException($"Image file not found: {imagePath}", imagePath);

        var (endpoint, resolvedModel) = ResolveBackend(backend, model, baseUrl);

        var imageBytes = await File.ReadAllBytesAsync(imagePath, cancellationToken);
        var mime = GetMimeType(imagePath);
        var imageDataUrl = $"data:{mime};base64,{Convert.ToBase64String(imageBytes)}";

        var payload = new JsonObject
        {
            ["model"] = resolvedModel,
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = new JsonArray
                    {
                        new JsonObject { ["type"] = "text", ["text"] = SystemPrompt },
                        new JsonObject
                        {
                            ["type"] = "image_url",
                            ["image_url"] = new JsonObject { ["url"] = imageDataUrl }
                        }
                    }
                }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        if (!string.IsNullOrWhiteSpace(apiKey))
            request.Headers.Add("Authorization", $"Bearer {apiKey}");
        request.Content = JsonContent.Create(payload);

        logger.LogDebug("Calling OCR backend {Backend} for {Image} ({Bytes} bytes, model {Model})",
            backend, Path.GetFileName(imagePath), imageBytes.Length, resolvedModel);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("OCR request failed ({Status}): {Body}", response.StatusCode, Truncate(body, 300));
            throw new InvalidOperationException($"OCR request failed with status {(int)response.StatusCode}: {Truncate(body, 300)}");
        }

        var text = ExtractText(body);
        if (string.IsNullOrWhiteSpace(text))
        {
            logger.LogWarning("OCR returned empty content for {Image}", Path.GetFileName(imagePath));
            return "[NO_TEXT]";
        }

        logger.LogDebug("OCR extracted {Length} chars from {Image}", text.Length, Path.GetFileName(imagePath));
        return text;
    }

    /// <summary>从 OpenAI 兼容响应体提取 choices[0].message.content。</summary>
    private static string ExtractText(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.TryGetProperty("choices", out var choices) &&
            choices.GetArrayLength() > 0 &&
            choices[0].TryGetProperty("message", out var message) &&
            message.TryGetProperty("content", out var content))
        {
            return content.ValueKind == JsonValueKind.Array
                ? string.Concat(content.EnumerateArray().Select(c => c.GetProperty("text").GetString()))
                : content.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    /// <summary>按扩展名推断图片 MIME 类型。</summary>
    private static string GetMimeType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        _ => "image/jpeg"
    };

    /// <summary>截断响应正文（日志用，避免把完整错误刷屏）。</summary>
    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}
