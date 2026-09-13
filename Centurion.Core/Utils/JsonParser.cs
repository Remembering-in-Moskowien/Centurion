using Newtonsoft.Json;

namespace Centurion.Core.Utils;

public static class JsonParser
{
    public static T Deserialize<T>(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        return JsonConvert.DeserializeObject<T>(json)
            ?? throw new InvalidOperationException($"Failed to deserialize JSON to {typeof(T).Name}.");
    }
}