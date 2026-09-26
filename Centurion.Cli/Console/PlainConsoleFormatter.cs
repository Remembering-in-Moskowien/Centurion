using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace Centurion.Cli.Console;

/// <summary>
/// Simple console formatter that stays consistent with the console log file:
/// emits `HH:mm:ss level: message` (no category), byte-for-byte matching
/// FileLoggerProvider's file line format; colors follow level/semantics only
/// ([SUCCESS] prefix green, warnings yellow, errors/critical red, otherwise white),
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
            LogLevel.Warning => "warn",
            LogLevel.Error => "fail",
            LogLevel.Critical => "crit",
            _ => "none"
        };

        var color = logEntry.LogLevel switch
        {
            LogLevel.Warning => System.ConsoleColor.Yellow,
            LogLevel.Error or LogLevel.Critical => System.ConsoleColor.Red,
            _ when message.StartsWith(SuccessPrefix, StringComparison.Ordinal) => System.ConsoleColor.Green,
            _ => System.ConsoleColor.White
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
