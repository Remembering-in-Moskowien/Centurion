// Centurion.Core/Abstractions/IConfirmPrompt.cs

namespace Centurion.Models.Console;

public interface IConfirmPrompt
{
    Task<bool> ConfirmAsync(string prompt);
}