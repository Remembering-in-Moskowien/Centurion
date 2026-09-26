using System.Text.Json;

namespace Centurion.Cli;

/// <summary>
/// 机器可读 JSON 输出端口：直接写 stdout（不经 Spectre 渲染/日志前缀），
/// 供 <c>--json</c> 模式被脚本安全消费。调用方应确保普通日志行已被抑制
/// （Program 启动时 --json 会把日志级别提升到 Warning）。
/// </summary>
public static class JsonOutput
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>把任意对象序列化为 JSON 并写入 stdout（无尾随换行的普通行）。</summary>
    public static void Write(object payload)
    {
        System.Console.Out.WriteLine(JsonSerializer.Serialize(payload, payload.GetType(), Options));
    }
}
