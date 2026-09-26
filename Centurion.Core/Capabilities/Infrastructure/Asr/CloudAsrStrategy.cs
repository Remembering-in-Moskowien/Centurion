using System.Net.Http.Headers;
using System.Text.Json;
using Centurion.Abstractions;
using Centurion.Abstractions.Strategy;
using Centurion.Models;
using Centurion.Models.Asr;
using Centurion.Models.Workflow;
using FFMpegCore;
using Microsoft.Extensions.Logging;
using Centurion.Core.Utils.Parsing;
namespace Centurion.Core.Capabilities.Infrastructure.Asr;

/// <summary>
/// 云端 ASR 转录策略：把音频上传到常见云语音识别 API 并返回词级时间戳。
/// 支持 OpenAI 兼容 multipart 协议（OpenAI / Groq / 阿里百炼 DashScope）与
/// Deepgram 原生协议；响应带 segment/word 时间戳时直接映射，仅有整段文本时
/// 按音频时长等分。供 <c>--transcriber openai|groq|dashscope|deepgram</c> 使用。
/// </summary>
public sealed class CloudAsrStrategy(HttpClient httpClient, ILogger<CloudAsrStrategy> logger) : ITranscriptionStrategy
{
    /// <summary>云端提供商（由工厂按引擎名设置）。</summary>
    public AsrProvider Provider { get; set; }

    /// <summary>提供商 API 密钥。</summary>
    public string? ApiKey { get; set; }

    /// <summary>自定义端点；为空时按提供商默认。</summary>
    public string? BaseUrl { get; set; }

    /// <summary>策略显示名称。</summary>
    public string StrategyName => $"Cloud ASR ({Provider})";

    /// <summary>
    /// 上传音频到云端 ASR，解析为词级时间戳列表。
    /// </summary>
    /// <param name="audioPath">输入音频文件路径（WAV 16kHz 单声道）。</param>
    /// <param name="language">语言代码，如 "en"、"zh"。</param>
    /// <param name="modelName">模型名；为空时按提供商默认。</param>
    /// <param name="initialPrompt">可选提示词（传给 OpenAI 兼容接口的 prompt 字段）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="device">忽略（云端推理无设备概念）。</param>
    /// <returns>词列表（含毫秒级起止时间）。</returns>
    /// <exception cref="InvalidOperationException">密钥缺失、请求失败或响应为空时抛出。</exception>
    public async Task<List<Word>> TranscribeAsync(
        string audioPath,
        string language,
        string modelName,
        string? initialPrompt = null,
        CancellationToken cancellationToken = default,
        InferenceDevice device = InferenceDevice.Auto)
    {
        if (!File.Exists(audioPath))
            throw new FileNotFoundException($"Audio file not found: {audioPath}", audioPath);

        if (string.IsNullOrWhiteSpace(ApiKey))
            throw new InvalidOperationException(
                $"Cloud ASR provider {Provider} requires an API key. Provide --asr-api-key <KEY>.");

        var (endpoint, defaultModel) = Provider switch
        {
            AsrProvider.OpenAI => (AsrEndpointParser.OpenAiEndpoint, "whisper-1"),
            AsrProvider.Groq => (AsrEndpointParser.GroqEndpoint, "whisper-large-v3"),
            AsrProvider.DashScope => (AsrEndpointParser.DashScopeEndpoint, "paraformer-realtime-v2"),
            AsrProvider.Deepgram => (AsrEndpointParser.DeepgramEndpoint, "nova-2"),
            _ => throw new ArgumentOutOfRangeException(nameof(Provider), Provider, "Unknown ASR provider")
        };
        endpoint = string.IsNullOrWhiteSpace(BaseUrl) ? endpoint : BaseUrl;
        var model = string.IsNullOrWhiteSpace(modelName) ? defaultModel : modelName;

        logger.LogDebug("Cloud ASR {Provider}: uploading {Audio} (model {Model}, language {Language})",
            Provider, Path.GetFileName(audioPath), model, language);

        var responseJson = Provider == AsrProvider.Deepgram
            ? await TranscribeDeepgramAsync(audioPath, endpoint, model, language, cancellationToken)
            : await TranscribeOpenAiCompatibleAsync(audioPath, endpoint, model, language, initialPrompt, cancellationToken);

        var words = ParseResponse(responseJson, language, audioPath, cancellationToken);        if (words.Count == 0)
            throw new InvalidOperationException($"Cloud ASR returned no words for '{Path.GetFileName(audioPath)}'.");

        logger.LogDebug("Cloud ASR {Provider} produced {Count} words", Provider, words.Count);
        return words;
    }

    /// <summary>调用 OpenAI 兼容 multipart 转录接口（OpenAI/Groq/DashScope）。</summary>
    private async Task<string> TranscribeOpenAiCompatibleAsync(
        string audioPath, string endpoint, string model, string language,
        string? initialPrompt, CancellationToken cancellationToken)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(await File.ReadAllBytesAsync(audioPath, cancellationToken)),
            "file", Path.GetFileName(audioPath));
        form.Add(new StringContent(model), "model");
        form.Add(new StringContent(language), "language");
        form.Add(new StringContent("verbose_json"), "response_format");
        form.Add(new StringContent("segment"), "timestamp_granularities[]");
        if (!string.IsNullOrWhiteSpace(initialPrompt))
            form.Add(new StringContent(initialPrompt), "prompt");

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
        request.Content = form;

        return await SendAndReadAsync(request, cancellationToken);
    }

    /// <summary>调用 Deepgram 原生接口（query 参数 + raw 音频）。</summary>
    private async Task<string> TranscribeDeepgramAsync(
        string audioPath, string endpoint, string model, string language, CancellationToken cancellationToken)
    {
        var uri = new UriBuilder(endpoint)
        {
            Query = $"model={Uri.EscapeDataString(model)}&language={Uri.EscapeDataString(language)}&punctuate=true&timestamps=true"
        }.Uri;

        using var request = new HttpRequestMessage(HttpMethod.Post, uri);
        request.Headers.Add("Authorization", $"Token {ApiKey}");
        var bytes = await File.ReadAllBytesAsync(audioPath, cancellationToken);
        request.Content = new ByteArrayContent(bytes);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");

        return await SendAndReadAsync(request, cancellationToken);
    }

    /// <summary>发送请求并读取响应体；非 2xx 抛出带截断正文的异常。</summary>
    private async Task<string> SendAndReadAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Cloud ASR request failed ({Status}): {Body}", response.StatusCode, Truncate(body, 300));
            throw new InvalidOperationException(
                $"Cloud ASR request failed with status {(int)response.StatusCode}: {Truncate(body, 300)}");
        }

        return body;
    }

    /// <summary>
    /// 解析响应为词级单元：
    /// OpenAI 形态 segments → 每段切词（真实时间戳）；仅 text → 整段等分；
    /// Deepgram words → 直接映射。
    /// </summary>
    private List<Word> ParseResponse(string json, string language, string audioPath, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        // Deepgram：results.channels[0].alternatives[0].words
        if (Provider == AsrProvider.Deepgram)
        {
            var words = new List<Word>();
            if (root.TryGetProperty("results", out var results) &&
                results.TryGetProperty("channels", out var channels) &&
                channels.GetArrayLength() > 0 &&
                channels[0].TryGetProperty("alternatives", out var alternatives) &&
                alternatives.GetArrayLength() > 0 &&
                alternatives[0].TryGetProperty("words", out var wordArray))
            {
                foreach (var item in wordArray.EnumerateArray())
                {
                    var text = item.GetProperty("word").GetString();
                    if (string.IsNullOrWhiteSpace(text))
                        continue;
                    var start = item.TryGetProperty("start", out var s) ? s.GetDouble() * 1000 : 0;
                    var end = item.TryGetProperty("end", out var e) ? e.GetDouble() * 1000 : start;
                    words.Add(new Word { Text = text, Start = start, End = end, Speaker = "UNKNOWN", Status = MappingStatus.Matched });
                }
            }
            return words;
        }

        // OpenAI 兼容：segments 或 text
        var durationMs = GetAudioDurationMsAsync(audioPath, cancellationToken).GetAwaiter().GetResult();

        if (root.TryGetProperty("segments", out var segments) && segments.GetArrayLength() > 0)
        {
            var words = new List<Word>();
            foreach (var segment in segments.EnumerateArray())
            {
                var text = segment.TryGetProperty("text", out var t) ? t.GetString() : null;
                if (string.IsNullOrWhiteSpace(text))
                    continue;
                var start = segment.TryGetProperty("start", out var s) ? s.GetDouble() * 1000 : 0;
                var end = segment.TryGetProperty("end", out var e) ? e.GetDouble() * 1000 : start;
                words.AddRange(SubtitleWordSplitter.SplitPlainWords(text, start, end, language));
            }
            return words;
        }

        if (root.TryGetProperty("text", out var textProp) && !string.IsNullOrWhiteSpace(textProp.GetString()))
            return SubtitleWordSplitter.SplitPlainWords(textProp.GetString()!, 0, durationMs, language);

        return [];
    }

    /// <summary>用 FFprobe 获取音频时长（毫秒）；失败时回退 0。</summary>
    private async Task<double> GetAudioDurationMsAsync(string audioPath, CancellationToken cancellationToken)
    {
        try
        {
            var analysis = await FFProbe.AnalyseAsync(audioPath);
            return analysis.Duration.TotalMilliseconds;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "FFprobe duration lookup failed for {Audio}", Path.GetFileName(audioPath));
            return 0;
        }
    }

    /// <summary>截断响应正文（日志用）。</summary>
    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}
