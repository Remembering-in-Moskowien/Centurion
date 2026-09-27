using System.Reflection;
using System.Text.Json;
using Centurion.Abstractions.Commands;
using Microsoft.Extensions.Logging;

namespace Centurion.Cli.Server;

/// <summary>
/// Request body parameter binder: parses a REST request body (a full
/// <see cref="CommandRequest"/> or a bare parameter object) into a
/// <see cref="CommandRequest"/>, then reflectively binds it onto the command's
/// Spectre Settings instance. Keys accept kebab-case / snake_case / camelCase;
/// nested objects expand to both "parent.child" and "child" candidates.
/// Priority: non-default values explicitly provided in the body &gt; defaults
/// (matching CLI command-line precedence).
/// </summary>
public static class ParameterBinder
{
    /// <summary>
    /// Parses a request body: supports both a full <see cref="CommandRequest"/> (with a parameters object) and a bare parameter object.
    /// </summary>
    /// <param name="body">The request body JSON.</param>
    /// <param name="commandName">The routed command name.</param>
    /// <returns>The command request.</returns>
    public static CommandRequest ParseBody(string body, string commandName)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var parameters = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("parameters", out var parametersElement) &&
            parametersElement.ValueKind == JsonValueKind.Object)
        {
            Flatten(parametersElement, string.Empty, parameters);
            return new CommandRequest(commandName, parameters);
        }

        Flatten(root, string.Empty, parameters);
        return new CommandRequest(commandName, parameters);
    }

    /// <summary>
    /// Reflectively binds request parameters onto Settings.
    /// Priority: non-default values explicitly set in the body &gt; defaults; unknown
    /// keys are logged as warnings and ignored (for forward compatibility).
    /// </summary>
    /// <param name="request">The command request.</param>
    /// <param name="settings">The settings instance (fresh, not yet bound to command line).</param>
    /// <param name="logger">Logs unknown keys and conversion failures.</param>
    public static void Apply(CommandRequest request, object settings, ILogger logger)
    {
        var properties = settings.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite)
            .ToList();

        var defaults = Activator.CreateInstance(settings.GetType());

        foreach (var (key, value) in request.Parameters)
        {
            var normalized = NormalizeKey(key);
            var property = FindProperty(properties, normalized);
            if (property is null)
            {
                logger.LogWarning("Unknown parameter '{Key}' for {Command}; ignoring.", key, request.Name);
                continue;
            }

            try
            {
                var converted = ConvertValue(value, property.PropertyType);
                if (converted is not null || Nullable.GetUnderlyingType(property.PropertyType) is not null)
                    property.SetValue(settings, converted);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to bind parameter '{Key}' ({Value}) to {Property}; ignoring.",
                    key, value, property.Name);
            }
        }
    }

    /// <summary>Normalizes a key: strips kebab/snake separators and lowercases.</summary>
    /// <param name="key">The raw key.</param>
    /// <returns>The normalized key.</returns>
    public static string NormalizeKey(string key) =>
        key.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

    /// <summary>Recursively flattens JSON objects/arrays into flat key-values (nested objects keep "parent.child" paths).</summary>
    private static void Flatten(JsonElement element, string prefix, Dictionary<string, object?> target)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var key = string.IsNullOrEmpty(prefix) ? property.Name : $"{prefix}.{property.Name}";
                    Flatten(property.Value, key, target);
                }
                break;

            default:
                target[prefix] = element.Clone();
                break;
        }
    }

    /// <summary>Matches properties by normalized name (including the tail segment of "parent.child" paths).</summary>
    private static PropertyInfo? FindProperty(List<PropertyInfo> properties, string normalizedKey)
    {
        foreach (var property in properties)
        {
            var attrNorm = NormalizeKey(property.Name);
            if (attrNorm == normalizedKey)
                return property;

            var dotIndex = normalizedKey.LastIndexOf('.');
            if (dotIndex >= 0)
            {
                var leaf = normalizedKey[(dotIndex + 1)..];
                if (attrNorm == leaf)
                    return property;
            }
        }
        return null;
    }

    /// <summary>Converts a JSON element to the target property type.</summary>
    private static object? ConvertValue(object? value, Type targetType)
    {
        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (value is JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Null)
                return null;

            if (underlying == typeof(string))
                return element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString();

            if (underlying == typeof(bool))
                return element.GetBoolean();

            if (underlying == typeof(int))
                return element.GetInt32();

            if (underlying == typeof(double))
                return element.GetDouble();

            if (underlying == typeof(float))
                return element.GetSingle();

            if (underlying.IsEnum)
                return Enum.Parse(underlying, element.GetString() ?? string.Empty, ignoreCase: true);

            if (underlying == typeof(FileInfo) && element.ValueKind == JsonValueKind.String)
                return new FileInfo(element.GetString() ?? string.Empty);

            if (underlying == typeof(DirectoryInfo) && element.ValueKind == JsonValueKind.String)
                return new DirectoryInfo(element.GetString() ?? string.Empty);

            if (underlying == typeof(DateTime))
                return element.GetDateTime();

            return element.Deserialize(underlying, JsonOptions.Value);
        }

        if (value is null)
            return null;

        if (underlying.IsInstanceOfType(value))
            return value;

        if (underlying.IsEnum)
            return Enum.Parse(underlying, value.ToString() ?? string.Empty, ignoreCase: true);

        if (underlying == typeof(FileInfo))
            return new FileInfo(value.ToString() ?? string.Empty);

        return Convert.ChangeType(value, underlying);
    }

    private static readonly Lazy<JsonSerializerOptions> JsonOptions = new(() => new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    });
}
