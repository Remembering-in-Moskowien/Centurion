using Centurion.Abstractions;
using Centurion.Abstractions.Utils;
using Centurion.Models.Console;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Centurion.Cli;

/// <summary>
/// Unified error output: logs a red failure line (matching file logs) and appends
/// actionable fix suggestions (yellow hint lines) so new users can self-service.
/// </summary>
public static class CliErrorPrinter
{
    /// <summary>
    /// Logs the failure and prints a fix suggestion.
    /// </summary>
    /// <param name="logger">The command logger.</param>
    /// <param name="ex">The caught exception.</param>
    /// <param name="context">Failure context description (written to the log).</param>
    public static void Print(ILogger logger, Exception ex, string context)
    {
        FailLogGate.Log(logger, ex, context);
        var hint = Suggest(ex);
        if (hint is not null)
            ConsoleServices.Output.WriteWarning(ConsoleServices.T("Suggestion: {0}", hint));
    }

    /// <summary>Maps common exceptions to actionable fix suggestions; falls back to a generic hint.</summary>
    public static string? Suggest(Exception ex) => ex switch
    {
        Centurion.Core.Capabilities.Managers.Media.ModelMissingException m =>
            ConsoleServices.T("Model '{0}' is not installed. Run: Centurion models install {0}", m.ModelName),
        FileNotFoundException => ConsoleServices.T("Input file not found. Check the path (or use the init wizard)"),
        DirectoryNotFoundException => ConsoleServices.T("Output directory not found. Create it or change the -o path"),
        JsonException => ConsoleServices.T("File is not valid JSON (or schema mismatch). Run: Centurion validate <file>"),
        UnauthorizedAccessException => ConsoleServices.T("No write permission. Check the output directory or use another path"),
        HttpRequestException => ConsoleServices.T("Network request failed. Check connectivity, or adjust --github-proxy / direct"),
        TaskCanceledException => ConsoleServices.T("Operation cancelled (timeout or user interrupt)"),
        OperationCanceledException => ConsoleServices.T("Operation cancelled"),
        _ => ConsoleServices.T("Add --verbose for detailed logs; if it recurs, attach the logs directory when reporting")
    };
}
