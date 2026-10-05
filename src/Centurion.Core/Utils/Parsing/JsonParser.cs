using Newtonsoft.Json;

namespace Centurion.Core.Utils.Parsing;

/// <summary>
/// JSON deserialization helper built on Newtonsoft.Json.
/// </summary>
public static class JsonParser
{
    /// <summary>
    /// Deserialize a JSON string into the given type.
    /// </summary>
    /// <typeparam name="T">The target type.</typeparam>
    /// <param name="json">The JSON string to deserialize.</param>
    /// <returns>The deserialized object.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the deserialization result is <see langword="null"/>.</exception>
    public static T Deserialize<T>(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        return JsonConvert.DeserializeObject<T>(json)
            ?? throw new InvalidOperationException($"Failed to deserialize JSON to {typeof(T).Name}.");
    }
}