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
/// Cloud ASR transcription strategy: uploads audio to common cloud speech-recognition
/// APIs and returns word-level timestamps. Supports the OpenAI-compatible multipart
/// protocol (OpenAI / Groq / Alibaba Bailian DashScope) and the Deepgram native
/// protocol; when the response carries segment/word timestamps they are mapped
/// directly, and when only full-text is available the duration is split evenly across
/// the audio. Used by <c>--transcriber openai|groq|dashscope|deepgram</c>.
/// </summary>
public sealed class CloudAsrStrategy(HttpClient httpClient, ILogger<CloudAsrStrategy> logger) : ITranscriptionStrategy
{
    /// <summary>Cloud provider (set by the factory based on the engine name).</summary>
    public AsrProvider Provider { get; set; }

    /// <summary>Provider API key.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Custom endpoint; falls back to the provider default when empty.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Strategy display name.</summary>
    public string StrategyName => $"Cloud ASR ({Provider})";

    /// <summary>
    /// Uploads audio to a cloud ASR and parses it into a list of word-level timestamps.
    /// </summary>
    /// <param name="audioPath">Input audio file path (WAV 16kHz mono).</param>
    /// <param name="language">Language code, e.g. "en", "zh".</param>
    /// <param name="modelName">Model name; falls back to the provider default when empty.</param>
    /// <param name="initialPrompt">Optional prompt (passed to the OpenAI-compatible endpoint's prompt field).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="device">Ignored (cloud inference has no device concept).</param>
    /// <returns>The word list (with millisecond start/end times).</returns>
    /// <exception cref="InvalidOperationException">Thrown when the key is missing, the request fails, or the response is empty.</exception>
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

    /// <summary>Calls the OpenAI-compatible multipart transcription endpoint (OpenAI/Groq/DashScope).</summary>
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

    /// <summary>Calls the Deepgram native endpoint (query params + raw audio).</summary>
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

    /// <summary>Sends the request and reads the response body; throws with a truncated body on non-2xx status.</summary>
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
    /// Parses the response into word-level units:
    /// OpenAI-shaped segments → split each segment into words (real timestamps); text only
    /// → split the whole duration evenly; Deepgram words → mapped directly.
    /// </summary>
    private List<Word> ParseResponse(string json, string language, string audioPath, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        // Deepgram: results.channels[0].alternatives[0].words
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

        // OpenAI-compatible: segments or text
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

    /// <summary>Gets the audio duration via FFprobe (milliseconds); falls back to 0 on failure.</summary>
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

    /// <summary>Truncates a response body (for logging).</summary>
    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}
