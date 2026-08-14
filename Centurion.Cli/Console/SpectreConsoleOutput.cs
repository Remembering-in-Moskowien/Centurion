// Centurion.Cli.Console/SpectreConsoleOutput.cs

using Centurion.Core.Abstractions;
using Spectre.Console;

namespace Centurion.Cli.Console;

public class SpectreConsoleOutput : IConsoleOutput
{
    public void Write(string message)
    {
        AnsiConsole.Write(message);
    }

    public void WriteLine(string message)
    {
        AnsiConsole.WriteLine(message);
    }

    public void WriteError(string message)
    {
        AnsiConsole.MarkupLine($"[red]{message.EscapeMarkup()}[/]");
    }

    public void WriteWarning(string message)
    {
        AnsiConsole.MarkupLine($"[yellow]{message.EscapeMarkup()}[/]");
    }

    public void WriteSuccess(string message)
    {
        AnsiConsole.MarkupLine($"[green]{message.EscapeMarkup()}[/]");
    }

    public void WriteInfo(string message)
    {
        AnsiConsole.MarkupLine($"[blue]{message.EscapeMarkup()}[/]");
    }

    public void WriteMarkup(string markup)
    {
        AnsiConsole.Write(markup);
    }

    public void WriteMarkupLine(string markup)
    {
        AnsiConsole.MarkupLine(markup);
    }
}