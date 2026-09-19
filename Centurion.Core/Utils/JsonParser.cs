using Newtonsoft.Json;

namespace Centurion.Core.Utils;

/// <summary>
/// 基于 Newtonsoft.Json 的 JSON 反序列化工具。
/// </summary>
public static class JsonParser
{
    /// <summary>
    /// 将 JSON 字符串反序列化为指定类型。
    /// </summary>
    /// <typeparam name="T">目标类型。</typeparam>
    /// <param name="json">待反序列化的 JSON 字符串。</param>
    /// <returns>反序列化得到的对象。</returns>
    /// <exception cref="InvalidOperationException">反序列化结果为 <see langword="null"/> 时抛出。</exception>
    public static T Deserialize<T>(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        return JsonConvert.DeserializeObject<T>(json)
            ?? throw new InvalidOperationException($"Failed to deserialize JSON to {typeof(T).Name}.");
    }
}