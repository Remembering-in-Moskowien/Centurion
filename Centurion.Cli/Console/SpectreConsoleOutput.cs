using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Centurion.Models.Console;
namespace Centurion.Cli.Console;

/// <summary>
/// Spectre.Console-based console output implementation.
/// <para>
/// Every output class goes to both the console (render layer) and ILogger (log layer,
/// and hence the logs file): info-class output renders as white/green lines and
/// forwards info logs (console side filters by category to avoid duplicate display);
/// warnings and errors go through the log channel only, rendered as yellow/red lines
/// by <see cref="PlainConsoleFormatter"/> for byte-identical file-log parity.
/// </para>
/// </summary>
public class SpectreConsoleOutput(ILogger<SpectreConsoleOutput> logger) : IConsoleOutput
{
    private readonly ILogger<SpectreConsoleOutput> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>--json mode switch: suppresses human-readable lines (logging still occurs); stdout keeps JSON only.</summary>
    public static bool SuppressHumanLines { get; set; }

    /// <summary>Builds the info prefix matching the log file (HH:mm:ss info:).</summary>
    private static string InfoPrefix => $"{DateTime.Now:HH:mm:ss} [blue]info[/]: ";

    /// <summary>Success badge + log-style prefix (✔ is console-only decoration; the log file records plain text).</summary>
    private static string SuccessPrefix => $"{DateTime.Now:HH:mm:ss} [green]{CliSymbols.Check}[/] ";

    /// <summary>
    /// Writes text to the console in white (no newline) and records an info log.
    /// No-newline concatenation semantics do not suit a prefix, so the line is kept as-is (no current call sites).
    /// </summary>
    /// <param name="message">The text to write.</param>
    public void Write(string message)
    {
        if (SuppressHumanLines) return;
        AnsiConsole.Markup($"{message.EscapeMarkup()}");
        _logger.LogInformation(message);
    }

    /// <summary>
    /// Writes a line of text to the console in white (with the log-style prefix) and records an info log.
    /// </summary>
    /// <param name="message">The text to write.</param>
    public void WriteLine(string message)
    {
        if (SuppressHumanLines) return;
        AnsiConsole.MarkupLine($"{InfoPrefix}{message.EscapeMarkup()}");
        _logger.LogInformation(message);
    }

    /// <summary>
    /// Outputs an error message (red, rendered via the log channel, byte-identical to the file log).
    /// </summary>
    /// <param name="message">The error message text.</param>
    public void WriteError(string message) => _logger.LogError(message);

    /// <summary>
    /// Outputs a warning message (yellow, rendered via the log channel, byte-identical to the file log).
    /// </summary>
    /// <param name="message">The warning message text.</param>
    public void WriteWarning(string message) => _logger.LogWarning(message);

    /// <summary>
    /// Writes a success message to the console in green (✔ badge + log-style prefix)
    /// and records a plain-text info log (no badge; the log file stays clean).
    /// </summary>
    /// <param name="message">The success message text.</param>
    public void WriteSuccess(string message)
    {
        if (SuppressHumanLines) return;
        AnsiConsole.MarkupLine($"{SuccessPrefix}{message.EscapeMarkup()}");
        _logger.LogInformation($"{message}");
    }

    /// <summary>
    /// Writes a hint line to the console in white (with the log-style prefix) and records an info log.
    /// </summary>
    /// <param name="message">The hint message text.</param>
    public void WriteInfo(string message)
    {
        if (SuppressHumanLines) return;
        AnsiConsole.MarkupLine($"{InfoPrefix}{message.EscapeMarkup()}");
        _logger.LogInformation(message);
    }

    /// <summary>
    /// Writes Spectre markup text to the console (no newline) and records an info log with markup stripped.
    /// </summary>
    /// <param name="markup">The Spectre markup string.</param>
    public void WriteMarkup(string markup)
    {
        if (SuppressHumanLines) return;
        AnsiConsole.Write(markup);
        _logger.LogInformation(StripMarkup(markup));
    }

    /// <summary>
    /// Writes a Spectre markup line to the console (with the log-style prefix) and records an info log with markup stripped.
    /// </summary>
    /// <param name="markup">The Spectre markup string.</param>
    public void WriteMarkupLine(string markup)
    {
        if (SuppressHumanLines) return;
        AnsiConsole.MarkupLine($"{InfoPrefix}{markup}");
        _logger.LogInformation(StripMarkup(markup));
    }

    /// <summary>Strips Spectre markup tags (e.g. [green], [/]), keeping plain text for logging.</summary>
    private static string StripMarkup(string markup) =>
        Regex.Replace(markup, @"\[[^\]]*\]", string.Empty);
}
