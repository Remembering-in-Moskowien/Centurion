// Centurion.Cli.Console/SpectreConfirmPrompt.cs
using Centurion.Core.Abstractions;
using Spectre.Console;

namespace Centurion.Cli.Console;

public class SpectreConfirmPrompt : IConfirmPrompt
{
    public Task<bool> ConfirmAsync(string prompt)
        => Task.FromResult(AnsiConsole.Confirm(prompt));
}