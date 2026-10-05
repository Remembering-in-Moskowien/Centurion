using System.Globalization;
using Microsoft.Extensions.Localization;

namespace Centurion.Core.Capabilities.Localization;

/// <summary>
/// Single-culture JSON string localizer: looks up the key in the current culture's
/// dictionary first, then in the English fallback dictionary; when neither hits it
/// returns the key itself (the key is the English default text), and placeholders are
/// formatted per string.Format rules.
/// </summary>
public sealed class JsonStringLocalizer(
    IReadOnlyDictionary<string, string> resources,
    IReadOnlyDictionary<string, string> fallbackResources) : IStringLocalizer
{
    private readonly IReadOnlyDictionary<string, string> _resources = resources;
    private readonly IReadOnlyDictionary<string, string> _fallback = fallbackResources;

    /// <summary>Gets the localized text by key (no arguments).</summary>
    public LocalizedString this[string name] => Get(name, []);

    /// <summary>Gets the localized text by key and placeholder arguments.</summary>
    public LocalizedString this[string name, params object[] arguments] => Get(name, arguments);

    /// <summary>Lists all key/value pairs in the current-culture resources.</summary>
    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) =>
        _resources.Select(pair => new LocalizedString(pair.Key, pair.Value, false));

    private LocalizedString Get(string name, object[] arguments)
    {
        if (_resources.TryGetValue(name, out var localized))
            return new LocalizedString(name, Format(localized, arguments), false);

        if (_fallback.TryGetValue(name, out var fallback))
            return new LocalizedString(name, Format(fallback, arguments), false);

        // Miss: the key itself is the English default text; flag ResourceNotFound so the caller can fall back
        return new LocalizedString(name, Format(name, arguments), true);
    }

    private static string Format(string template, object[] arguments) =>
        arguments.Length == 0
            ? template
            : string.Format(CultureInfo.InvariantCulture, template, arguments);
}
