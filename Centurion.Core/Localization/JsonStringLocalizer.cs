using System.Globalization;
using Microsoft.Extensions.Localization;

namespace Centurion.Core.Localization;

/// <summary>
/// 单个文化的 JSON 字符串本地化器：先在当前文化字典查找，其次英文回退字典，
/// 均未命中时返回 key 本身（key 即英文默认文本），占位符按 string.Format 规则格式化。
/// </summary>
public sealed class JsonStringLocalizer(
    IReadOnlyDictionary<string, string> resources,
    IReadOnlyDictionary<string, string> fallbackResources) : IStringLocalizer
{
    private readonly IReadOnlyDictionary<string, string> _resources = resources;
    private readonly IReadOnlyDictionary<string, string> _fallback = fallbackResources;

    /// <summary>按 key 取本地化文本（无参数）。</summary>
    public LocalizedString this[string name] => Get(name, []);

    /// <summary>按 key 与占位符参数取本地化文本。</summary>
    public LocalizedString this[string name, params object[] arguments] => Get(name, arguments);

    /// <summary>列出当前文化资源中的全部键值对。</summary>
    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) =>
        _resources.Select(pair => new LocalizedString(pair.Key, pair.Value, false));

    private LocalizedString Get(string name, object[] arguments)
    {
        if (_resources.TryGetValue(name, out var localized))
            return new LocalizedString(name, Format(localized, arguments), false);

        if (_fallback.TryGetValue(name, out var fallback))
            return new LocalizedString(name, Format(fallback, arguments), false);

        // 未命中：key 即英文默认文本，标记 ResourceNotFound 供调用方回退
        return new LocalizedString(name, Format(name, arguments), true);
    }

    private static string Format(string template, object[] arguments) =>
        arguments.Length == 0
            ? template
            : string.Format(CultureInfo.InvariantCulture, template, arguments);
}
