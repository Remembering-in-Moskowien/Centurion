using Spectre.Console;
using Centurion.Models.Console;
namespace Centurion.Cli.Console;

/// <summary>
/// Spectre.Console-based interactive confirmation prompt implementation.
/// </summary>
public class SpectreConfirmPrompt : IConfirmPrompt
{
    /// <summary>
    /// Shows a confirmation prompt to the user and returns the choice.
    /// </summary>
    /// <param name="prompt">The prompt text shown to the user.</param>
    /// <returns>true when confirmed, false when cancelled.</returns>
    public Task<bool> ConfirmAsync(string prompt)
    {
        // --agent mode: never block on an interactive prompt; emit a WARN line and decline
        // (matches the [y/N] default) so destructive operations require an explicit flag.
        if (SpectreConsoleOutput.AgentMode)
        {
            ConsoleServices.Output.WriteWarning(ConsoleServices.T("Confirmation declined (--agent mode): {0}", prompt));
            return Task.FromResult(false);
        }
        return Task.FromResult(AnsiConsole.Confirm(prompt));
    }
}