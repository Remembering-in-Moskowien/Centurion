using Centurion.Core.Services.Base;
using Centurion.Core.Services.Dto;
using System.Text.Json;

namespace Centurion.Core.Services;

/// <summary>
/// SAT 语义分句服务（基于 wtpsplit）
/// 不管理模型文件，由 Python 库自行处理。
/// </summary>
public class SatSplitService : PythonServiceBase
{
    protected override string ScriptName => "sat_split_service.py";
    protected override int StartupDelayMs => 8000;

    /// <summary>
    /// 构造函数，不注入任何 ModelManager。
    /// </summary>
    public SatSplitService() : base(null)
    {
    }

    /// <summary>
    /// 对词级时间戳进行语义分句，返回句子列表。
    /// </summary>
    public async Task<List<SatSplitResult>> SplitAsync(
        List<WordTiming> words,
        int maxLength = 80,
        int targetLength = 50,
        int spreadRange = 10,
        CancellationToken ct = default)
    {
        var payload = new
        {
            words = words.Select(w => new { text = w.Text.Trim(), start = w.Start, end = w.End }),
            max_length = maxLength,
            target_length = targetLength,
            spread_range = spreadRange
        };
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        var response = await SendRequestAsync(json, ct);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        using var doc = JsonDocument.Parse(response);
        if (!doc.RootElement.TryGetProperty("sentences", out var sentencesElement))
            return [];

        var sentences = JsonSerializer.Deserialize<List<SatSplitResult>>(sentencesElement.GetRawText(), options);
        return sentences ?? [];
    }
}