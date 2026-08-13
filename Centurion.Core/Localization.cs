using System.Globalization;
using System.Resources;

namespace Centurion.Core;

public class Localization
{
    private static readonly ResourceManager ResourceManager = new(typeof(Localization));

    public static CultureInfo CurrentCulture { get; private set; } = DetectCurrentCulture();

    public static void SetLanguage(string language)
    {
        if (string.IsNullOrWhiteSpace(language))
            throw new ArgumentException(
                ResourceManager.GetString("LanguageCannotBeNullOrEmpty", CultureInfo.InvariantCulture) ??
                "Language cannot be null or empty.", nameof(language));

        language = language.Trim().ToLowerInvariant();
        if (language.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            CurrentCulture = new CultureInfo("zh");
        }
        else if (language.StartsWith("en", StringComparison.OrdinalIgnoreCase))
        {
            CurrentCulture = new CultureInfo("en");
        }
        else
        {
            var messageTemplate = ResourceManager.GetString("UnsupportedLanguage", CultureInfo.InvariantCulture) ??
                                  "Unsupported language: {0}";
            throw new ArgumentException(string.Format(messageTemplate, language), nameof(language));
        }
    }

    private static CultureInfo DetectCurrentCulture()
    {
        var lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return lang.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? new CultureInfo("zh")
            : new CultureInfo("en");
    }

    public static string Get(string key, params object[] args)
    {
        var template = ResourceManager.GetString(key, CurrentCulture);
        if (string.IsNullOrEmpty(template)) template = ResourceManager.GetString(key, CultureInfo.InvariantCulture);

        if (string.IsNullOrEmpty(template)) return key;

        return args.Length == 0 ? template : string.Format(template, args);
    }
}