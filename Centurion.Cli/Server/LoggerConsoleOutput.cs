using Centurion.Core.Capabilities.Infrastructure;using Microsoft.Extensions.Logging;
using Centurion.Models.Console;
namespace Centurion.Cli.Server;

/// <summary>
/// Console output adapter: forwards messages emitted inside commands via
/// <see cref="ConsoleServices"/> to the Server ILogger (information level), so command
/// execution is captured by server logs and the log file.
/// </summary>
public sealed class LoggerConsoleOutput(ILogger logger) : IConsoleOutput
{
    /// <summary>Writes a text chunk without a newline (information level).</summary>
    /// <param name="message">The text content.</param>
    public void Write(string message) => logger.LogInformation("{Message}", message);

    /// <summary>Writes a line of text with a newline (information level).</summary>
    /// <param name="message">The text content.</param>
    public void WriteLine(string message) => logger.LogInformation("{Message}", message);

    /// <summary>Outputs a line in error style (error level).</summary>
    /// <param name="message">The error text.</param>
    public void WriteError(string message) => logger.LogError("{Message}", message);

    /// <summary>Outputs a line in warning style (warning level).</summary>
    /// <param name="message">The warning text.</param>
    public void WriteWarning(string message) => logger.LogWarning("{Message}", message);

    /// <summary>Outputs a line in success style (information level).</summary>
    /// <param name="message">The success text.</param>
    public void WriteSuccess(string message) => logger.LogInformation("{Message}", message);

    /// <summary>Outputs a line in plain information style (information level).</summary>
    /// <param name="message">The information text.</param>
    public void WriteInfo(string message) => logger.LogInformation("{Message}", message);

    /// <summary>Writes rich-text markup (information level, markup preserved as-is).</summary>
    /// <param name="markup">The rich-text content.</param>
    public void WriteMarkup(string markup) => logger.LogInformation("{Markup}", markup);

    /// <summary>Writes rich-text markup with a newline (information level, markup preserved as-is).</summary>
    /// <param name="markup">The rich-text content.</param>
    public void WriteMarkupLine(string markup) => logger.LogInformation("{Markup}", markup);
}
