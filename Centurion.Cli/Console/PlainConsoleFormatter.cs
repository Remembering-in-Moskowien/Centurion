using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace Centurion.Cli.Console;

/// <summary>
/// Simple console formatter that stays consistent with the console log file:
/// emits `HH:mm:ss LEVEL: message` (no category), byte-for-byte matching
/// FileLoggerProvider's file line format; colors follow level/semantics only
/// (WARN badge gold-tone, ERR/CRIT red, [SUCCESS] prefix green, otherwise white),
/// and colors are dropped automatically when output is redirected. Every console
/// line is identical to its counterpart in the log file.
/// </summary>
public sealed class PlainConsoleFormatter : ConsoleFormatter
{
    private const string SuccessPrefix = "[SUCCESS] ";

    /// <summary>Creates the formatter registered under the name "plain".</summary>
    public PlainConsoleFormatter() : base("plain")
    {
    }

    /// <summary>Formats a log entry as a single console line (matching the file log format).</summary>
    public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        var message = logEntry.Formatter(logEntry.State, logEntry.Exception);
        if (logEntry.Exception is not null)
            message = string.IsNullOrEmpty(message)
                ? logEntry.Exception.ToString()
                : $"{message}{Environment.NewLine}{logEntry.Exception}";
        if (string.IsNullOrEmpty(message))
            return;

        var levelText = logEntry.LogLevel switch
        {
            LogLevel.Trace or LogLevel.Debug => "dbug",
            LogLevel.Information => "info",
            LogLevel.Warning => "WARN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "crit",
            _ => "none"
        };

        var color = logEntry.LogLevel switch
        {
            LogLevel.Warning => Centurion.Models.Console.CliPalette.WarningColor,
            LogLevel.Error => Centurion.Models.Console.CliPalette.ErrorColor,
            LogLevel.Critical => Centurion.Models.Console.CliPalette.CriticalColor,
            _ when message.StartsWith(SuccessPrefix, StringComparison.Ordinal) => Centurion.Models.Console.CliPalette.SuccessColor,
            _ => Centurion.Models.Console.CliPalette.InfoColor
        };

        var line = $"{DateTime.Now:HH:mm:ss} {levelText}: {message}";
        var original = System.Console.ForegroundColor;
        try
        {
            System.Console.ForegroundColor = color;
            textWriter.WriteLine(line);
        }
        finally
        {
            System.Console.ForegroundColor = original;
        }
    }
}
