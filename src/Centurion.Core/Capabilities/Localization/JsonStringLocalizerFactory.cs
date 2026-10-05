using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Localization;

namespace Centurion.Core.Capabilities.Localization;

/// <summary>
/// JSON resource-file-based localization factory: reads a flat key/value dictionary from
/// <c>Localization/{culture}.json</c> in the app root (the key is the English default
/// text, the value is the translation), falling back to English (the key itself) when
/// the language is missing. The files are editable external resources — adding a
/// language only requires dropping in the corresponding <c>xx-XX.json</c>, with no
/// recompile.
/// </summary>
public sealed class JsonStringLocalizerFactory : IStringLocalizerFactory
{
    private readonly string _directory;
    private readonly ConcurrentDictionary<string, JsonStringLocalizer> _cache = new();

    /// <summary>Creates a JSON localizer factory.</summary>
    /// <param name="directory">JSON resource directory; uses Localization under the app root when null.</param>
    public JsonStringLocalizerFactory(string? directory = null)
    {
        _directory = directory ?? Path.Combine(AppContext.BaseDirectory, "Localization");
    }

    /// <summary>Creates a localizer by type (type info is ignored; resources are read uniformly for the current UI culture).</summary>
    public IStringLocalizer Create(Type resourceSource) => CreateForCulture(CultureInfo.CurrentUICulture.Name);

    /// <summary>Creates a localizer by base name (base name is ignored; resources are read uniformly for the current UI culture).</summary>
    public IStringLocalizer Create(string baseName) => CreateForCulture(CultureInfo.CurrentUICulture.Name);

    /// <summary>Creates a localizer by base name and location (both are ignored; resources are read uniformly for the current UI culture).</summary>
    public IStringLocalizer Create(string baseName, string location) => CreateForCulture(CultureInfo.CurrentUICulture.Name);

    private JsonStringLocalizer CreateForCulture(string cultureName) =>
        _cache.GetOrAdd(cultureName, name =>
        {
            var current = Load(name);
            var fallback = Load("en");
            return new JsonStringLocalizer(current, fallback);
        });

    private IReadOnlyDictionary<string, string> Load(string cultureName)
    {
        var path = Path.Combine(_directory, $"{cultureName}.json");
        if (!File.Exists(path))
            return new Dictionary<string, string>();

        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
                ?? new Dictionary<string, string>();
        }
        catch (JsonException)
        {
            // When a resource file is corrupt, fall back to English (the key) instead of aborting the program
            return new Dictionary<string, string>();
        }
    }
}
