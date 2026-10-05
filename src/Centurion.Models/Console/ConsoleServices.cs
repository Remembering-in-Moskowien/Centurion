using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.Extensions.Localization;

namespace Centurion.Models.Console;

/// <summary>
/// Facade for console output ports.
/// Provides unified console output, progress, and confirmation to non-DI components such as models, tools, and operators.
/// The CLI entry point injects the concrete adapters at startup.
/// </summary>
public static class ConsoleServices
{
    /// <summary>Console text output port; defaults to a no-op implementation until an adapter is injected.</summary>
    [NotNull] public static IConsoleOutput Output { get; set; } = new NullConsoleOutput();
    /// <summary>Progress display port; defaults to a no-op implementation until an adapter is injected.</summary>
    [NotNull] public static IProgressReporter Progress { get; set; } = new NullProgressReporter();
    /// <summary>User confirmation prompt port; defaults to an auto-confirming no-op implementation until an adapter is injected.</summary>
    [NotNull] public static IConfirmPrompt Confirm { get; set; } = new NullConfirmPrompt();

    /// <summary>JSON localizer injected by the CLI entry point; when null, messages use their English source text.</summary>
    public static IStringLocalizer? Localizer { get; set; }

    /// <summary>
    /// Gets localized text by looking up the English source key in the JSON resources for the current language.
    /// Returns the key, with placeholder formatting, when no localizer is configured or no translation is found.
    /// </summary>
    /// <param name="key">Message key containing the English source text and optional placeholders such as {0}.</param>
    /// <param name="args">Placeholder values.</param>
    /// <returns>Localized text.</returns>
    public static string T(string key, params object?[] args)
    {
        if (Localizer is null)
            return args.Length == 0 ? key : string.Format(CultureInfo.InvariantCulture, key, args);

        var localized = args.Length == 0 ? Localizer[key] : Localizer[key, (object[])args];
        if (!localized.ResourceNotFound)
            return localized.Value;

        return args.Length == 0 ? key : string.Format(CultureInfo.InvariantCulture, key, args);
    }
}
