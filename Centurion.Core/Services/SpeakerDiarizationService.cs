using Centurion.Core.Services.Base;
using System.Text.Json;

namespace Centurion.Core.Services;

public class SpeakerDiarizationService : PythonServiceBase
{
    private readonly string _modelPath;

    protected override string ScriptName => "diarize_service.py";
    protected override int StartupDelayMs => 30000;

    public SpeakerDiarizationService() : base(null) // 保持原有构造函数
    {
        _modelPath = Path.Combine(AppContext.BaseDirectory, "models", "wespeaker_cache");
    }

    public async Task<List<string>> DiarizeSegmentsAsync(
        string audioFile,
        List<(double StartMs, double EndMs)> segments,
        int numSpeakers = 0,
        CancellationToken ct = default)
    {
        var payload = new
        {
            audio_file = audioFile,
            segments = segments.Select(s => new { start = s.StartMs / 1000.0, end = s.EndMs / 1000.0 }),
            num_speakers = numSpeakers,
            model_path = _modelPath
        };
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        // 移除调试输出
        var response = await SendRequestAsync(json, ct);
        var result = JsonSerializer.Deserialize<DiarizationResult>(response, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return result?.Segments?.Select(s => s.Speaker).ToList() ?? new List<string>();
    }

    private class DiarizationResult
    {
        public List<SegmentResult> Segments { get; set; } = new();
    }

    private class SegmentResult
    {
        public string Speaker { get; set; } = "";
    }
}