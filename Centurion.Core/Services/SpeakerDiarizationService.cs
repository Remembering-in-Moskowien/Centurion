using Centurion.Core.Services.Base;
using Centurion.Core.Services.Dto;
using System.Text.Json;

namespace Centurion.Core.Services;

/// <summary>
/// 说话人分割服务（基于 diarize 库）
/// </summary>
public class SpeakerDiarizationService() : PythonServiceBase(null)
{
    protected override string ScriptName => "diarize_service.py";
    protected override int StartupDelayMs => 3000; // 模型加载耗时

    /// <summary>
    /// 对音频进行说话人分割
    /// </summary>
    /// <param name="audioFile">音频文件路径</param>
    /// <param name="numSpeakers">说话人数（0 表示自动检测）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>说话人分段列表</returns>
    public async Task<List<SpeakerSegment>> DiarizeAsync(
        string audioFile,
        int numSpeakers = 0,
        CancellationToken ct = default)
    {
        var payload = new
        {
            audio_file = audioFile,
            num_speakers = numSpeakers
        };
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        var response = await SendRequestAsync(json, ct);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var result = JsonSerializer.Deserialize<SpeakerDiarizationResult>(response, options);
        return result?.Segments ?? [];
    }
}