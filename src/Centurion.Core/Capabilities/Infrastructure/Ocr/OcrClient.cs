using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Centurion.Core.Capabilities.Infrastructure.Ocr;

/// <summary>OCR inference backend.</summary>
public enum OcrBackend
{
    /// <summary>Zhipu open-platform GLM-OCR (cloud, requires an API key).</summary>
    Zhipu,

    /// <summary>Local Ollama vision model (qwen2.5vl / llava, etc.; no key required).</summary>
    Ollama,

    /// <summary>Local llama-server (GGUF vision model, no key required; the service must already be running).</summary>
    LlamaCpp,

    /// <summary>Local RapidOCR (RapidOcrNet, PaddleOCR ONNX, CPU-only, multilingual).</summary>
    RapidOcr
}

/// <summary>
/// Multi-backend OCR client: performs subtitle-level OCR on a single image via an
/// OpenAI-compatible chat/completions endpoint, using a multimodal message (text
/// instruction + base64 image). Supports both cloud (Zhipu GLM-OCR) and local
/// inference (Ollama / llama-server) paths.
/// </summary>
public sealed class OcrClient(HttpClient httpClient, ILogger<OcrClient> logger)
{
    /// <summary>Zhipu open-platform default chat/completions endpoint.</summary>
    public const string ZhipuBaseUrl = "https://open.bigmodel.cn/api/paas/v4/chat/completions";

    /// <summary>Zhipu default OCR model (switch to a vision model such as glm-4v-plus if your account has not enabled this one).</summary>
    public const string ZhipuDefaultModel = "glm-ocr";

    /// <summary>Local Ollama default endpoint (OpenAI-compatible).</summary>
    public const string OllamaBaseUrl = "http://localhost:11434/v1/chat/completions";

    /// <summary>Ollama default vision model.</summary>
    public const string OllamaDefaultModel = "qwen2.5vl:7b";

    /// <summary>Local llama-server default endpoint (OpenAI-compatible).</summary>
    public const string LlamaCppBaseUrl = "http://127.0.0.1:8080/v1/chat/completions";

    /// <summary>llama-server default model placeholder (the OpenAI-compatible implementation ignores the model content).</summary>
    public const string LlamaCppDefaultModel = "local-model";

    /// <summary>OCR instruction: output only the subtitle text, one entry per line, with no explanation.</summary>
    private const string SystemPrompt =
        "You are a subtitle OCR engine. Extract ALL visible subtitle/caption text from the image. " +
        "Output ONLY the subtitle lines, one subtitle per line, preserving the original language exactly. " +
        "Do not describe the image, do not add numbering, do not translate, do not add explanations. " +
        "If there is no subtitle text, output exactly: [NO_TEXT]";

    /// <summary>
    /// Resolves the final endpoint and model by backend: explicit configuration wins,
    /// otherwise the backend defaults are used.
    /// </summary>
    /// <param name="backend">OCR backend.</param>
    /// <param name="model">Explicit model; uses the backend default when empty.</param>
    /// <param name="baseUrl">Explicit endpoint; uses the backend default when empty.</param>
    /// <returns>(endpoint, model).</returns>
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
        OcrBackend.RapidOcr => (string.Empty, string.IsNullOrWhiteSpace(model) ? "rapidocr" : model),
        _ => throw new ArgumentOutOfRangeException(nameof(backend), backend, "Unknown OCR backend")
    };

    /// <summary>
    /// Probes whether the local OCR service is online (GET /models, compatible with the
    /// OpenAI-compatible endpoints of Ollama and llama-server). Cloud backends return
    /// true (no pre-probe; failures surface naturally from the request).
    /// </summary>
    /// <param name="backend">OCR backend.</param>
    /// <param name="baseUrl">Explicit endpoint; uses the backend default when empty.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Whether the service is reachable.</returns>
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
    /// Runs OCR on a single image and returns the extracted subtitle text (may span
    /// multiple lines; "[NO_TEXT]" when there are no subtitles).
    /// </summary>
    /// <param name="imagePath">Image file path (jpg/png, etc.).</param>
    /// <param name="backend">OCR backend (cloud/local).</param>
    /// <param name="model">OCR model name; uses the backend default when empty.</param>
    /// <param name="apiKey">API key; required only for Zhipu cloud, may be empty for local backends.</param>
    /// <param name="baseUrl">Endpoint address; uses the backend default when empty.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The extracted subtitle text.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the cloud key is missing, the request fails, or the response has no content.</exception>
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

    /// <summary>Extracts choices[0].message.content from an OpenAI-compatible response body.</summary>
    private static string ExtractText(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"OCR backend returned a non-JSON response: {Truncate(json, 300)}", ex);
        }

        using (document)
        {
            if (document.RootElement.TryGetProperty("choices", out var choices) &&
                choices.GetArrayLength() > 0 &&
                choices[0].TryGetProperty("message", out var message) &&
                message.TryGetProperty("content", out var content))
            {
                return content.ValueKind == JsonValueKind.Array
                    ? string.Concat(content.EnumerateArray().Select(c =>
                        c.TryGetProperty("text", out var textElement)
                            ? textElement.GetString()
                            : string.Empty))
                    : content.GetString() ?? string.Empty;
            }

            return string.Empty;
        }
    }

    /// <summary>Infers the image MIME type from the file extension.</summary>
    private static string GetMimeType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        _ => "image/jpeg"
    };

    /// <summary>Truncates a response body (for logging, to avoid flooding the console with the full error).</summary>
    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}
