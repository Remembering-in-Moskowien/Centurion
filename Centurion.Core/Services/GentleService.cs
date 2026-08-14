using System.Net.Http;
using System.Text;
using System.Text.Json;
using Centurion.Core.Services.Dto;

namespace Centurion.Core.Services;

/// <summary>
/// Gentle 强制对齐服务（HTTP 客户端）
/// </summary>
public class GentleService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    /// <summary>
    /// 构造函数，可指定服务地址（默认 http://localhost:8765）
    /// </summary>
    public GentleService(string baseUrl = "http://localhost:8765")
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromMinutes(10);
    }

    /// <summary>
    /// 执行强制对齐
    /// </summary>
    public async Task<GentleAlignmentResult> AlignAsync(string audioPath, string transcriptPath, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(await File.ReadAllBytesAsync(audioPath, ct)), "audio", Path.GetFileName(audioPath) },
            { new ByteArrayContent(await File.ReadAllBytesAsync(transcriptPath, ct)), "transcript", Path.GetFileName(transcriptPath) }
        };

        var response = await _httpClient.PostAsync($"{_baseUrl}/transcriptions?async=false", form, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<GentleAlignmentResult>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        return result ?? new GentleAlignmentResult();
    }

    public void Dispose() => _httpClient.Dispose();
}