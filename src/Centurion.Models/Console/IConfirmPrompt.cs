namespace Centurion.Models.Console;

/// <summary>Port for displaying a confirmation prompt in the command line and waiting for the response.</summary>
public interface IConfirmPrompt
{
    /// <summary>Asynchronously displays a confirmation prompt and gets the user's response.</summary>
    /// <param name="prompt">Prompt text to display.</param>
    /// <returns>True if the user confirms; otherwise false.</returns>
    Task<bool> ConfirmAsync(string prompt);
}