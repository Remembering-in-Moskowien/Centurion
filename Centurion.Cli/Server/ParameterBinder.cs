using System.Reflection;
using System.Text.Json;
using Centurion.Abstractions.Commands;
using Microsoft.Extensions.Logging;

namespace Centurion.Cli.Server;

/// <summary>
/// 请求体参数绑定器：把 REST 请求体（完整 <see cref="CommandRequest"/> 或裸参数对象）
/// 解析为 <see cref="CommandRequest"/>，再反射绑定到命令的 Spectre Settings 实例。
/// 键支持 kebab-case / snake_case / camelCase 三种写法；嵌套对象展开为「父.子」与「子」两个候选。
/// 优先级：请求体显式提供的非默认值 &gt; 默认值（与 CLI 命令行优先级语义一致）。
/// </summary>
public static class ParameterBinder
{
    /// <summary>
    /// 解析请求体：支持完整 <see cref="CommandRequest"/>（含 parameters 对象）或裸参数对象两种形态。
    /// </summary>
    /// <param name="body">请求体 JSON。</param>
    /// <param name="commandName">路由命令名。</param>
    /// <returns>命令请求。</returns>
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
    /// 把命令请求的参数反射绑定到 Settings。
    /// 优先级：请求体显式设置的非默认值 &gt; 默认值；未知键记录警告后忽略（便于向前兼容）。
    /// </summary>
    /// <param name="request">命令请求。</param>
    /// <param name="settings">命令设置实例（新建，尚未绑定命令行）。</param>
    /// <param name="logger">记录未知键与转换失败的日志器。</param>
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

    /// <summary>归一化键：去 kebab/snake 分隔符并转小写。</summary>
    /// <param name="key">原始键。</param>
    /// <returns>归一化键。</returns>
    public static string NormalizeKey(string key) =>
        key.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

    /// <summary>递归把 JSON 对象/数组展开为扁平键值（嵌套对象保留「父.子」路径）。</summary>
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

    /// <summary>在属性集合中按归一化名匹配（含「父.子」路径的后段匹配）。</summary>
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

    /// <summary>把 JSON 元素转换为目标属性类型。</summary>
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
